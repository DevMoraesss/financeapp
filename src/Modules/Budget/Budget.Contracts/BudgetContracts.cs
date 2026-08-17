namespace FinanceMove.Modules.Budget.Contracts;

/// <summary>Faixa de consumo do limite. A API decide; a UI so pinta (fluxos-usuario secao 3).</summary>
public enum BudgetStatus
{
    /// <summary>Abaixo de 80% do limite.</summary>
    Normal = 1,

    /// <summary>Entre 80% e 100%.</summary>
    Warning = 2,

    /// <summary>Acima de 100%.</summary>
    Over = 3,
}

/// <summary>
/// Uma categoria com limite definido e o quanto dela ja foi consumido no mes.
/// </summary>
/// <param name="Difference">
/// Positivo indica quanto ainda pode gastar; negativo, quanto passou do limite.
/// </param>
public sealed record BudgetItemDto(
    Guid CategoryId,
    string Category,
    string Color,
    decimal Limit,
    decimal Spent,
    decimal Percent,
    BudgetStatus Status,
    decimal Difference);

public sealed record BudgetSummaryDto(
    decimal TotalSpent,
    decimal TotalLimit,
    int CategoriesNearLimit,
    int CategoriesOverLimit);

public sealed record BudgetOverviewDto(
    string Month,
    BudgetSummaryDto Summary,
    IReadOnlyList<BudgetItemDto> Items);

public interface IBudgetService
{
    Task<BudgetOverviewDto> GetOverviewAsync(Guid userId, string month, CancellationToken cancellationToken = default);

    /// <summary>Define ou atualiza o limite de uma categoria. Idempotente.</summary>
    Task<BudgetItemDto> SetLimitAsync(
        Guid userId,
        Guid categoryId,
        decimal monthlyLimit,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default);
}
