using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;

namespace FinanceMove.Api.Endpoints;

internal static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder routes)
    {
        var categories = routes.MapGroup("/api/v1/categories").RequireAuthorization();

        // O filtro de enum chega como string e passa pelo EnumParsing: query string nao usa o
        // serializador JSON, entao declarar CategoryKind? aqui quebraria em "expense".
        categories.MapGet("/", async (
            string? type,
            bool? includeArchived,
            ICurrentUser user,
            ICategoryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(new
            {
                categories = await service.ListAsync(
                    user.Id,
                    EnumParsing.ParseOptional<CategoryKind>(type, "type"),
                    includeArchived ?? false,
                    cancellationToken),
            }));

        categories.MapPost("/", async (
            CreateCategoryRequest request,
            ICurrentUser user,
            ICategoryService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateAsync(user.Id, request, cancellationToken);
            return Results.Created($"/api/v1/categories/{created.Id}", created);
        });

        categories.MapPut("/{id:guid}", async (
            Guid id,
            UpdateCategoryRequest request,
            ICurrentUser user,
            ICategoryService service,
            CancellationToken cancellationToken) =>
        {
            var updated = await service.UpdateAsync(user.Id, id, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        categories.MapPost("/{id:guid}/archive", async (
            Guid id,
            ICurrentUser user,
            ICategoryService service,
            CancellationToken cancellationToken) =>
        {
            var archived = await service.ArchiveAsync(user.Id, id, cancellationToken);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        var transactions = routes.MapGroup("/api/v1/transactions").RequireAuthorization();

        transactions.MapGet("/", async (
            string? month,
            Guid? accountId,
            Guid? categoryId,
            string? type,
            string? status,
            string? search,
            int? page,
            int? size,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var filter = new TransactionFilter(
                month,
                accountId,
                categoryId,
                EnumParsing.ParseOptional<TransactionType>(type, "type"),
                EnumParsing.ParseOptional<TransactionStatus>(status, "status"),
                search,
                page ?? 1,
                size ?? 50);

            return Results.Ok(await service.ListAsync(user.Id, filter, cancellationToken));
        });

        transactions.MapPost("/", async (
            CreateTransactionRequest request,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(user.Id, request, cancellationToken);
            return Results.Created($"/api/v1/transactions/{result.Transaction.Id}", result);
        });

        transactions.MapPost("/installments", async (
            CreateInstallmentsRequest request,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateInstallmentsAsync(user.Id, request, cancellationToken);
            return Results.Created("/api/v1/transactions", created);
        });

        transactions.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var transaction = await service.FindAsync(user.Id, id, cancellationToken);
            return transaction is null ? Results.NotFound() : Results.Ok(transaction);
        });

        transactions.MapPut("/{id:guid}", async (
            Guid id,
            UpdateTransactionRequest request,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(user.Id, id, request, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        transactions.MapDelete("/{id:guid}", async (
            Guid id,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var deleted = await service.DeleteAsync(user.Id, id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        transactions.MapPost("/{id:guid}/confirm", async (
            Guid id,
            ConfirmTransactionRequest? request,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ConfirmAsync(user.Id, id, request?.Amount, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        transactions.MapDelete("/{id:guid}/discard", async (
            Guid id,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var discarded = await service.DiscardAsync(user.Id, id, cancellationToken);
            return discarded ? Results.NoContent() : Results.NotFound();
        });

        routes.MapDelete("/api/v1/installment-groups/{id:guid}", async (
            Guid id,
            ICurrentUser user,
            ITransactionService service,
            CancellationToken cancellationToken) =>
        {
            var removed = await service.DeleteInstallmentGroupAsync(user.Id, id, cancellationToken);
            return Results.Ok(new { removedInstallments = removed });
        }).RequireAuthorization();

        var recurrences = routes.MapGroup("/api/v1/recurrences").RequireAuthorization();

        recurrences.MapGet("/", async (
            ICurrentUser user,
            IRecurrenceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(new { recurrences = await service.ListAsync(user.Id, cancellationToken) }));

        recurrences.MapPost("/", async (
            CreateRecurrenceRequest request,
            ICurrentUser user,
            IRecurrenceService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateAsync(user.Id, request, cancellationToken);
            return Results.Created($"/api/v1/recurrences/{created.Id}", created);
        });

        recurrences.MapPost("/{id:guid}/deactivate", async (
            Guid id,
            ICurrentUser user,
            IRecurrenceService service,
            CancellationToken cancellationToken) =>
        {
            var deactivated = await service.DeactivateAsync(user.Id, id, cancellationToken);
            return deactivated ? Results.NoContent() : Results.NotFound();
        });
    }
}

internal sealed record ConfirmTransactionRequest(decimal? Amount);
