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
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var target = month ?? $"{clock.Today.Year:D4}-{clock.Today.Month:D2}";

            var accountList = await accounts.ListAsync(user.Id, includeArchived: false, cancellationToken);
            var balances = (await transactions.GetBalancesAsync(user.Id, clock.Today, cancellationToken))
                .ToDictionary(balance => balance.AccountId, balance => balance.Balance);

            var summary = await transactions.GetMonthSummaryAsync(user.Id, target, cancellationToken);
            var byCategory = await transactions.SumExpensesByCategoryAsync(user.Id, target, cancellationToken);
            var recent = await transactions.GetRecentAsync(user.Id, 10, cancellationToken);
            var pending = await transactions.GetPendingAsync(user.Id, cancellationToken);

            var accountPayload = accountList.Select(account => new
            {
                id = account.Id,
                name = account.Name,
                type = account.Type,
                currentBalance = balances.GetValueOrDefault(account.Id, account.InitialBalance),
            }).ToList();

            return Results.Ok(new
            {
                totalBalance = Money.Round(accountPayload.Sum(account => account.currentBalance)),
                accounts = accountPayload,
                month = summary,
                expensesByCategory = byCategory,
                recentTransactions = recent,
                pending,
            });
        }).RequireAuthorization();
    }
}
