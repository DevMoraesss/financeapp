using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;

namespace FinanceMove.Api.Endpoints;

/// <summary>
/// Faturas de cartao (docs/api.md secao 8). Tela que o prototipo nao tinha: nasceu da decisao de
/// modelar cartao de credito com ciclo de fatura (SPEC D5).
/// </summary>
internal static class StatementEndpoints
{
    public static void MapStatementEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/cards/{cardId:guid}/statements").RequireAuthorization();

        group.MapGet("/", async (
            Guid cardId,
            int? months,
            ICurrentUser user,
            IStatementService statements,
            CancellationToken cancellationToken) =>
            Results.Ok(new
            {
                statements = await statements.ListAsync(user.Id, cardId, months ?? 6, cancellationToken),
            }));

        group.MapGet("/{month}", async (
            Guid cardId,
            string month,
            ICurrentUser user,
            IStatementService statements,
            CancellationToken cancellationToken) =>
        {
            var statement = await statements.GetAsync(user.Id, cardId, month, cancellationToken);
            return statement is null ? Results.NotFound() : Results.Ok(statement);
        });

        group.MapPost("/{month}/pay", async (
            Guid cardId,
            string month,
            PayStatementRequest request,
            ICurrentUser user,
            IStatementService statements,
            CancellationToken cancellationToken) =>
        {
            var result = await statements.PayAsync(user.Id, cardId, month, request.SourceAccountId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Created("/api/v1/transactions", result);
        });
    }
}

internal sealed record PayStatementRequest(Guid SourceAccountId);
