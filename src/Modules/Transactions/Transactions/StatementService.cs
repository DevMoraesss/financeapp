using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Transactions;

/// <summary>
/// Faturas de cartao de credito (SPEC secao 5.3).
/// </summary>
/// <remarks>
/// Nao existe tabela de fatura: ela e uma consulta sobre as compras, montada pelo ciclo do cartao.
/// Isso elimina a classe inteira de bug "fatura dessincronizada das compras", pelo mesmo motivo
/// que o saldo nao e armazenado.
/// </remarks>
internal sealed class StatementService(
    TransactionsDbContext context,
    IAccountsQuery accountsQuery,
    ITransactionService transactions,
    ITransactionsQuery transactionsQuery,
    IClock clock) : IStatementService
{
    public async Task<IReadOnlyList<StatementSummaryDto>> ListAsync(
        Guid userId,
        Guid cardId,
        int months = 6,
        CancellationToken cancellationToken = default)
    {
        var card = await accountsQuery.FindAsync(userId, cardId, cancellationToken);

        if (card is null || card.Type != AccountType.CreditCard)
        {
            return [];
        }

        var (closing, due) = RequireCycle(card);
        var today = clock.Today;

        // Ancorada na fatura em que uma compra de hoje cairia, mostra as anteriores e a proxima.
        var current = ParseMonth(CardCycle.StatementMonthFor(closing, due, today));
        var first = current.AddMonths(-(Math.Max(months, 2) - 2));

        var summaries = new List<StatementSummaryDto>();

        for (var month = first; month <= current.AddMonths(1); month = month.AddMonths(1))
        {
            summaries.Add(await BuildSummaryAsync(userId, card, closing, due, month, cancellationToken));
        }

        return summaries.OrderByDescending(summary => summary.Month).ToList();
    }

    public async Task<StatementDetailDto?> GetAsync(
        Guid userId,
        Guid cardId,
        string month,
        CancellationToken cancellationToken = default)
    {
        var card = await accountsQuery.FindAsync(userId, cardId, cancellationToken);

        if (card is null || card.Type != AccountType.CreditCard)
        {
            return null;
        }

        var (closing, due) = RequireCycle(card);
        var summary = await BuildSummaryAsync(userId, card, closing, due, ParseMonth(month), cancellationToken);

        var purchases = await context.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Where(transaction => transaction.UserId == userId
                && transaction.AccountId == cardId
                && transaction.Type == TransactionType.Expense
                && transaction.StatementMonth == summary.Month)
            .OrderByDescending(transaction => transaction.Date)
            .ToListAsync(cancellationToken);

        var accounts = (await accountsQuery.ListAsync(userId, includeArchived: true, cancellationToken))
            .ToDictionary(account => account.Id);

        return new StatementDetailDto(
            summary,
            purchases.Select(transaction => Mapping.ToDto(transaction, accounts)).ToList());
    }

    public async Task<PayStatementResultDto?> PayAsync(
        Guid userId,
        Guid cardId,
        string month,
        Guid sourceAccountId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetAsync(userId, cardId, month, cancellationToken);

        if (detail is null)
        {
            return null;
        }

        if (detail.Summary.Status == StatementStatus.Paid)
        {
            throw DomainException.Conflict(
                $"A fatura {detail.Summary.Month} ja foi paga.",
                "statement-already-paid");
        }

        if (detail.Summary.Status == StatementStatus.Open)
        {
            throw DomainException.Unprocessable(
                $"A fatura {detail.Summary.Month} ainda esta aberta. Ela fecha em {detail.Summary.ClosingDate:dd/MM/yyyy}.",
                "statement-still-open");
        }

        if (detail.Summary.Total <= 0m)
        {
            throw DomainException.Unprocessable("Esta fatura nao tem valor a pagar.", "empty-statement");
        }

        // Pagar fatura e uma TRANSFERENCIA, nunca uma despesa: o gasto ja contou na data da
        // compra, e contar de novo dobraria a despesa do usuario (SPEC D6).
        var result = await transactions.CreateAsync(
            userId,
            new CreateTransactionRequest(
                TransactionType.Transfer,
                detail.Summary.Total,
                clock.Today,
                $"Pagamento da fatura {detail.Summary.Month}",
                sourceAccountId,
                cardId,
                null),
            cancellationToken);

        // Marca a transferencia com o mes da fatura, que e como sabemos que ela foi quitada.
        await context.Transactions
            .Where(transaction => transaction.Id == result.Transaction.Id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(transaction => transaction.StatementMonth, detail.Summary.Month),
                cancellationToken);

        var today = clock.Today;

        return new PayStatementResultDto(
            result.Transaction with { StatementMonth = detail.Summary.Month },
            StatementStatus.Paid,
            await transactionsQuery.GetBalanceAsync(userId, sourceAccountId, today, cancellationToken),
            await transactionsQuery.GetBalanceAsync(userId, cardId, today, cancellationToken));
    }

    private async Task<StatementSummaryDto> BuildSummaryAsync(
        Guid userId,
        AccountSummary card,
        int closingDay,
        int dueDay,
        DateOnly month,
        CancellationToken cancellationToken)
    {
        var cycle = CardCycle.For(closingDay, dueDay, month);

        var total = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId
                && transaction.AccountId == card.Id
                && transaction.Type == TransactionType.Expense
                && transaction.StatementMonth == cycle.Month)
            .SumAsync(transaction => (decimal?)transaction.Amount, cancellationToken) ?? 0m;

        var paid = await context.Transactions
            .AsNoTracking()
            .AnyAsync(
                transaction => transaction.UserId == userId
                    && transaction.Type == TransactionType.Transfer
                    && transaction.DestinationAccountId == card.Id
                    && transaction.StatementMonth == cycle.Month,
                cancellationToken);

        var status = paid
            ? StatementStatus.Paid
            : clock.Today <= cycle.ClosingDate
                ? StatementStatus.Open
                : StatementStatus.Closed;

        return new StatementSummaryDto(
            cycle.Month,
            cycle.ClosingDate,
            cycle.DueDate,
            cycle.PeriodStart,
            cycle.PeriodEnd,
            status,
            Money.Round(total));
    }

    private static (int ClosingDay, int DueDay) RequireCycle(AccountSummary card)
    {
        if (card.ClosingDay is not { } closing || card.DueDay is not { } due)
        {
            throw DomainException.Unprocessable(
                $"O cartao \"{card.Name}\" esta sem dia de fechamento ou de vencimento.",
                "missing-card-cycle");
        }

        return (closing, due);
    }

    private static DateOnly ParseMonth(string month)
    {
        var (from, _) = MonthRange.Parse(month);
        return from;
    }
}
