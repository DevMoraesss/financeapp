using System.Linq.Expressions;
using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Transactions;

/// <summary>
/// Leituras agregadas expostas para os outros modulos e para o dashboard.
/// </summary>
/// <remarks>
/// O saldo e sempre derivado, nunca armazenado (SPEC D4): e o saldo inicial da conta, obtido do
/// modulo Accounts pelo contrato, somado aos lancamentos confirmados ate a data.
/// </remarks>
internal sealed class TransactionsQuery(
    TransactionsDbContext context,
    IAccountsQuery accountsQuery) : ITransactionsQuery
{
    public async Task<IReadOnlyList<AccountBalanceDto>> GetBalancesAsync(
        Guid userId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var accounts = await accountsQuery.ListAsync(userId, includeArchived: true, cancellationToken);

        // Cartao e divida, nao dinheiro: o saldo dele e tudo que foi comprado e ainda nao foi pago,
        // INCLUSIVE as parcelas futuras (e o "limite utilizado" que o banco mostra). Nas contas,
        // lancamento com data futura ainda nao aconteceu e fica de fora (SPEC secao 5.1).
        var cardIds = accounts
            .Where(account => account.Type == AccountType.CreditCard)
            .Select(account => account.Id)
            .ToList();

        // Duas agregacoes no banco em vez de trazer as linhas para a memoria. A primeira cobre a
        // conta de origem (receita entra, despesa e transferencia saem); a segunda cobre a ponta
        // que recebe a transferencia.
        var outgoing = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId
                && transaction.Status == TransactionStatus.Confirmed
                && (transaction.Date <= asOf || cardIds.Contains(transaction.AccountId)))
            .GroupBy(transaction => transaction.AccountId)
            .Select(group => new
            {
                AccountId = group.Key,
                Amount = group.Sum(transaction =>
                    transaction.Type == TransactionType.Income ? transaction.Amount : -transaction.Amount),
            })
            .ToDictionaryAsync(row => row.AccountId, row => row.Amount, cancellationToken);

        var incoming = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId
                && transaction.Status == TransactionStatus.Confirmed
                && transaction.DestinationAccountId != null
                && (transaction.Date <= asOf || cardIds.Contains(transaction.DestinationAccountId.Value)))
            .GroupBy(transaction => transaction.DestinationAccountId!.Value)
            .Select(group => new { AccountId = group.Key, Amount = group.Sum(transaction => transaction.Amount) })
            .ToDictionaryAsync(row => row.AccountId, row => row.Amount, cancellationToken);

        return accounts
            .Select(account => new AccountBalanceDto(
                account.Id,
                Money.Round(
                    account.InitialBalance
                    + outgoing.GetValueOrDefault(account.Id)
                    + incoming.GetValueOrDefault(account.Id))))
            .ToList();
    }

    public async Task<decimal> GetBalanceAsync(
        Guid userId,
        Guid accountId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var balances = await GetBalancesAsync(userId, asOf, cancellationToken);
        return balances.FirstOrDefault(balance => balance.AccountId == accountId)?.Balance ?? 0m;
    }

    public async Task<MonthSummaryDto> GetMonthSummaryAsync(
        Guid userId,
        string month,
        CancellationToken cancellationToken = default)
    {
        // Transferencia nao entra: mover dinheiro entre contas proprias nao e receita nem
        // despesa, senao o relatorio mentiria (SPEC D6). Despesa de cartao conta no mes da fatura.
        var totals = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId
                && transaction.Status == TransactionStatus.Confirmed
                && transaction.Type != TransactionType.Transfer)
            .Where(MonthRange.ReferenceFilter(month))
            .GroupBy(transaction => transaction.Type)
            .Select(group => new { Type = group.Key, Amount = group.Sum(transaction => transaction.Amount) })
            .ToListAsync(cancellationToken);

        var income = totals.FirstOrDefault(total => total.Type == TransactionType.Income)?.Amount ?? 0m;
        var expenses = totals.FirstOrDefault(total => total.Type == TransactionType.Expense)?.Amount ?? 0m;

        return new MonthSummaryDto(Money.Round(income), Money.Round(expenses), Money.Round(income - expenses));
    }

    public async Task<IReadOnlyList<CategoryTotalDto>> SumExpensesByCategoryAsync(
        Guid userId,
        string month,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId
                && transaction.Status == TransactionStatus.Confirmed
                && transaction.Type == TransactionType.Expense
                && transaction.CategoryId != null)
            .Where(MonthRange.ReferenceFilter(month))
            .GroupBy(transaction => new { transaction.CategoryId, transaction.Category!.Name, transaction.Category.Color })
            .Select(group => new
            {
                group.Key.CategoryId,
                group.Key.Name,
                group.Key.Color,
                Amount = group.Sum(transaction => transaction.Amount),
            })
            .ToListAsync(cancellationToken);

        var total = rows.Sum(row => row.Amount);

        return rows
            .OrderByDescending(row => row.Amount)
            .Select(row => new CategoryTotalDto(
                row.CategoryId!.Value,
                row.Name,
                row.Color,
                Money.Round(row.Amount),
                total == 0m ? 0m : Math.Round(row.Amount / total * 100m, 1, MidpointRounding.AwayFromZero)))
            .ToList();
    }

    public Task<bool> AccountHasTransactionsAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        context.Transactions
            .AsNoTracking()
            .AnyAsync(
                transaction => transaction.UserId == userId
                    && (transaction.AccountId == accountId || transaction.DestinationAccountId == accountId),
                cancellationToken);

    public async Task<IReadOnlyList<TransactionDto>> GetRecentAsync(
        Guid userId,
        int count,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        // "Ultimas" e o que ja aconteceu: parcela de novembro nao e uma transacao recente.
        var transactions = await context.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Where(transaction => transaction.UserId == userId
                && transaction.Status == TransactionStatus.Confirmed
                && transaction.Date <= asOf)
            .OrderByDescending(transaction => transaction.Date)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

        var accounts = await LoadAccountsAsync(userId, cancellationToken);
        return transactions.Select(transaction => Mapping.ToDto(transaction, accounts)).ToList();
    }

    public async Task<IReadOnlyList<TransactionDto>> GetPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var transactions = await context.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Where(transaction => transaction.UserId == userId && transaction.Status == TransactionStatus.Pending)
            .OrderBy(transaction => transaction.Date)
            .ToListAsync(cancellationToken);

        var accounts = await LoadAccountsAsync(userId, cancellationToken);
        return transactions.Select(transaction => Mapping.ToDto(transaction, accounts)).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, AccountSummary>> LoadAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var accounts = await accountsQuery.ListAsync(userId, includeArchived: true, cancellationToken);
        return accounts.ToDictionary(account => account.Id);
    }
}

/// <summary>Converte "AAAA-MM" no primeiro e no ultimo dia daquele mes.</summary>
internal static class MonthRange
{
    public static (DateOnly From, DateOnly To) Parse(string month)
    {
        if (!TryParse(month, out var range))
        {
            throw DomainException.Unprocessable($"Mes invalido: \"{month}\". Use o formato AAAA-MM.", "invalid-month");
        }

        return range;
    }

    public static bool TryParse(string month, out (DateOnly From, DateOnly To) range)
    {
        range = default;

        var parts = month.Split('-');

        if (parts.Length != 2
            || !int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var monthNumber)
            || year is < 1900 or > 2999
            || monthNumber is < 1 or > 12)
        {
            return false;
        }

        var from = new DateOnly(year, monthNumber, 1);
        range = (from, from.AddMonths(1).AddDays(-1));
        return true;
    }

    /// <summary>
    /// Lancamentos que pertencem ao mes: despesa de cartao pelo mes da FATURA (quando o dinheiro
    /// sai), todo o resto pela data (SPEC secao 5.3, revista em 25/09/2026).
    /// </summary>
    /// <remarks>
    /// Compra de 14/08 em 3x num cartao que vence dia 10: as parcelas contam em setembro, outubro e
    /// novembro, e nao em agosto. O pagamento da fatura e transferencia, entao nao conta de novo.
    /// E a mesma regra no resumo do mes, no grafico por categoria, no orcamento e na lista.
    /// </remarks>
    public static Expression<Func<Transaction, bool>> ReferenceFilter(string month)
    {
        var (from, to) = Parse(month);
        var key = $"{from.Year:D4}-{from.Month:D2}";

        return transaction =>
            (transaction.Type == TransactionType.Expense
                && transaction.StatementMonth != null
                && transaction.StatementMonth == key)
            || ((transaction.Type != TransactionType.Expense || transaction.StatementMonth == null)
                && transaction.Date >= from
                && transaction.Date <= to);
    }
}
