using FinanceMove.Modules.Budget.Contracts;
using FinanceMove.Shared;

namespace FinanceMove.Api.Endpoints;

internal static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/budgets").RequireAuthorization();

        group.MapGet("/", async (
            string? month,
            ICurrentUser user,
            IBudgetService budgets,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var target = month ?? $"{clock.Today.Year:D4}-{clock.Today.Month:D2}";
            return Results.Ok(await budgets.GetOverviewAsync(user.Id, target, cancellationToken));
        });

        group.MapPut("/{categoryId:guid}", async (
            Guid categoryId,
            SetBudgetRequest request,
            ICurrentUser user,
            IBudgetService budgets,
            CancellationToken cancellationToken) =>
            Results.Ok(await budgets.SetLimitAsync(user.Id, categoryId, request.MonthlyLimit, cancellationToken)));

        group.MapDelete("/{categoryId:guid}", async (
            Guid categoryId,
            ICurrentUser user,
            IBudgetService budgets,
            CancellationToken cancellationToken) =>
        {
            var removed = await budgets.RemoveAsync(user.Id, categoryId, cancellationToken);
            return removed ? Results.NoContent() : Results.NotFound();
        });
    }
}

internal sealed record SetBudgetRequest(decimal MonthlyLimit);
