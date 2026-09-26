using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;

namespace FinanceMove.Api.Endpoints;

/// <summary>
/// Dashboard (docs/api.md secao 10).
/// </summary>
/// <remarks>
/// Uma chamada so, de proposito. O prototipo faria cinco requisicoes para montar a mesma tela e
/// sofreria com N+1; aqui o host compoe os dados dos modulos e devolve tudo pronto, com os
/// numeros ja calculados para a UI nao ter de somar nada.
/// <para>
/// Contas (debito) e cartoes (credito) vem SEPARADOS: o limite do cartao nao e dinheiro, e somar
/// divida de cartao com saldo de conta num numero so escondia de onde vinha cada real (decidido
/// com o usuario em 25/09/2026).
/// </para>
/// </remarks>
internal static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/dashboard", async (
            string? month,
            ICurrentUser user,
            IAccountsQuery accounts,
            ITransactionsQuery transactions,
            IStatementService statements,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var target = month ?? $"{clock.Today.Year:D4}-{clock.Today.Month:D2}";

            var accountList = await accounts.ListAsync(user.Id, includeArchived: false, cancellationToken);
            var balances = (await transactions.GetBalancesAsync(user.Id, clock.Today, cancellationToken))
                .ToDictionary(balance => balance.AccountId, balance => balance.Balance);

            var summary = await transactions.GetMonthSummaryAsync(user.Id, target, cancellationToken);
            var byCategory = await transactions.SumExpensesByCategoryAsync(user.Id, target, cancellationToken);
            var recent = await transactions.GetRecentAsync(user.Id, 10, clock.Today, cancellationToken);
            var pending = await transactions.GetPendingAsync(user.Id, cancellationToken);

            decimal BalanceOf(AccountSummary account) => balances.GetValueOrDefault(account.Id, account.InitialBalance);

            var accountPayload = accountList.Select(account => new
            {
                id = account.Id,
                name = account.Name,
                type = account.Type,
                currentBalance = BalanceOf(account),
            }).ToList();

            var debitAccounts = accountList.Where(account => account.Type != AccountType.CreditCard).ToList();
            var cards = new List<object>();
            var cardsOwed = 0m;

            foreach (var card in accountList.Where(account => account.Type == AccountType.CreditCard))
            {
                // Saldo de cartao negativo = divida. Saldo positivo (pagou a mais) nao e divida.
                var owed = Money.Round(Math.Max(0m, -BalanceOf(card)));
                cardsOwed += owed;

                cards.Add(new
                {
                    id = card.Id,
                    name = card.Name,
                    owed,
                    creditLimit = card.CreditLimit,
                    availableLimit = card.CreditLimit is { } limit ? Money.Round(limit - owed) : (decimal?)null,
                    currentStatement = CurrentStatement(
                        await statements.ListAsync(user.Id, card.Id, 6, cancellationToken)),
                });
            }

            var accountsBalance = Money.Round(debitAccounts.Sum(BalanceOf));

            return Results.Ok(new
            {
                accountsBalance,
                cardsOwed = Money.Round(cardsOwed),

                // Mantido para quem ja consome o contrato: contas menos o que se deve nos cartoes.
                totalBalance = Money.Round(accountsBalance - cardsOwed),
                accounts = accountPayload,
                cards,
                month = summary,
                expensesByCategory = byCategory,
                recentTransactions = recent,
                pending,
            });
        }).RequireAuthorization();
    }

    /// <summary>
    /// A fatura que o usuario vai pagar a seguir: a mais antiga fechada e ainda nao paga; se nao
    /// houver, a que esta aberta agora.
    /// </summary>
    private static StatementSummaryDto? CurrentStatement(IReadOnlyList<StatementSummaryDto> statements)
    {
        var ordered = statements.OrderBy(statement => statement.Month, StringComparer.Ordinal).ToList();

        return ordered.FirstOrDefault(statement => statement.Status == StatementStatus.Closed && statement.Total > 0m)
            ?? ordered.FirstOrDefault(statement => statement.Status == StatementStatus.Open);
    }
}
