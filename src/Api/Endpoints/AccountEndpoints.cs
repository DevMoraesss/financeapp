using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;

namespace FinanceMove.Api.Endpoints;

internal static class AccountEndpoints
{
    /// <summary>
    /// Limite disponivel = limite menos o que ja foi comprado e nao pago (inclusive parcelas
    /// futuras, que ja ocupam o limite no banco).
    /// </summary>
    private static AccountPayload ToPayload(AccountSummary account, decimal currentBalance) => new(
        account.Id,
        account.Name,
        account.Type,
        account.InitialBalance,
        currentBalance,
        account.ClosingDay,
        account.DueDay,
        account.CreditLimit,
        account.CreditLimit is { } limit ? Money.Round(limit + Math.Min(0m, currentBalance)) : null,
        account.Archived);

    public static void MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/accounts").RequireAuthorization();

        // A listagem cruza dois modulos: Accounts e dono do cadastro, Transactions calcula o
        // saldo. Quem compoe e o host, e nao um modulo lendo a tabela do outro
        // (docs/arquitetura.md secao 3).
        group.MapGet("/", async (
            bool? includeArchived,
            ICurrentUser user,
            IAccountsQuery accounts,
            ITransactionsQuery transactions,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var list = await accounts.ListAsync(user.Id, includeArchived ?? false, cancellationToken);
            var balances = (await transactions.GetBalancesAsync(user.Id, clock.Today, cancellationToken))
                .ToDictionary(balance => balance.AccountId, balance => balance.Balance);

            var payload = list.Select(account => ToPayload(
                account,
                balances.GetValueOrDefault(account.Id, account.InitialBalance))).ToList();

            return Results.Ok(new
            {
                accounts = payload,
                totalBalance = Money.Round(payload.Sum(account => account.CurrentBalance)),
            });
        });

        group.MapPost("/", async (
            CreateAccountRequest request,
            ICurrentUser user,
            IAccountsService accounts,
            CancellationToken cancellationToken) =>
        {
            var created = await accounts.CreateAsync(user.Id, request, cancellationToken);
            return Results.Created($"/api/v1/accounts/{created.Id}", created);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentUser user,
            IAccountsQuery accounts,
            ITransactionsQuery transactions,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var account = await accounts.FindAsync(user.Id, id, cancellationToken);

            // Conta de outro usuario responde 404, e nunca 403: 403 confirmaria que o id existe
            // (SPEC US-14).
            if (account is null)
            {
                return Results.NotFound();
            }

            var balance = await transactions.GetBalanceAsync(user.Id, id, clock.Today, cancellationToken);
            return Results.Ok(ToPayload(account, balance));
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateAccountRequest request,
            ICurrentUser user,
            IAccountsService accounts,
            CancellationToken cancellationToken) =>
        {
            var updated = await accounts.UpdateAsync(user.Id, id, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapPost("/{id:guid}/archive", async (
            Guid id,
            ICurrentUser user,
            IAccountsService accounts,
            CancellationToken cancellationToken) =>
        {
            var archived = await accounts.ArchiveAsync(user.Id, id, cancellationToken);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICurrentUser user,
            IAccountsService accounts,
            ITransactionsQuery transactions,
            CancellationToken cancellationToken) =>
        {
            // Conta que ja teve lancamento nunca some: o historico financeiro nao se apaga
            // (modelo-de-dados secao 5.3).
            if (await transactions.AccountHasTransactionsAsync(user.Id, id, cancellationToken))
            {
                throw DomainException.Conflict(
                    "Esta conta ja tem lancamentos e nao pode ser excluida. Arquive-a.",
                    "account-has-transactions");
            }

            var deleted = await accounts.DeleteAsync(user.Id, id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/{id:guid}/adjust-balance", async (
            Guid id,
            AdjustBalanceRequest request,
            ICurrentUser user,
            ITransactionService transactions,
            CancellationToken cancellationToken) =>
        {
            var result = await transactions.AdjustBalanceAsync(user.Id, id, request.RealBalance, cancellationToken);

            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/transactions/{result.Transaction.Id}", new
                {
                    createdTransaction = result.Transaction,
                    currentBalance = result.AccountBalance,
                });
        });
    }
}

internal sealed record AdjustBalanceRequest(decimal RealBalance);

/// <summary>Conta como a API devolve. Em cartao, o saldo e a divida (negativo) e o limite e so informativo.</summary>
internal sealed record AccountPayload(
    Guid Id,
    string Name,
    AccountType Type,
    decimal InitialBalance,
    decimal CurrentBalance,
    short? ClosingDay,
    short? DueDay,
    decimal? CreditLimit,
    decimal? AvailableLimit,
    bool Archived);
