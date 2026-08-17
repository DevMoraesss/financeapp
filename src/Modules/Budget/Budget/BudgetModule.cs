using FinanceMove.Modules.Budget.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using FinanceMove.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceMove.Modules.Budget;

/// <summary>Limite mensal de gasto de uma categoria (SPEC secao 5.7).</summary>
public sealed class BudgetLimit
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// Referencia logica para a categoria, que pertence ao modulo Transactions. Sem chave
    /// estrangeira de proposito: modulos nao se amarram pelo banco (modelo-de-dados secao 1.1).
    /// </summary>
    public Guid CategoryId { get; set; }

    public decimal MonthlyLimit { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public const string Schema = "budget";

    public DbSet<BudgetLimit> Budgets => Set<BudgetLimit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<BudgetLimit>(entity =>
        {
            entity.ToTable("budget", table =>
                table.HasCheckConstraint("ck_budget_limit", "monthly_limit > 0"));

            entity.HasKey(budget => budget.Id);
            entity.Property(budget => budget.MonthlyLimit).HasColumnType("numeric(14,2)").IsRequired();
            entity.Property(budget => budget.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(budget => budget.UpdatedAt).HasDefaultValueSql("now()");

            // Um limite por categoria, por usuario.
            entity.HasIndex(budget => new { budget.UserId, budget.CategoryId })
                  .IsUnique()
                  .HasDatabaseName("ux_budget_category");
        });

        modelBuilder.UseSnakeCaseNames();
    }
}

internal sealed class BudgetService(
    BudgetDbContext context,
    ITransactionsQuery transactionsQuery,
    ICategoryService categories,
    IClock clock) : IBudgetService
{
    private const decimal WarningThreshold = 80m;

    public async Task<BudgetOverviewDto> GetOverviewAsync(
        Guid userId,
        string month,
        CancellationToken cancellationToken = default)
    {
        var limits = await context.Budgets
            .AsNoTracking()
            .Where(budget => budget.UserId == userId)
            .ToListAsync(cancellationToken);

        // O modulo Budget NAO le a tabela de transacoes: pede as somas pelo contrato. E a
        // fronteira do ADR na pratica, e a direcao da dependencia esta certa (Budget depende de
        // Transactions; Transactions nem sabe que orcamento existe).
        var spentByCategory = await transactionsQuery.SumExpensesByCategoryAsync(userId, month, cancellationToken);
        var allCategories = await categories.ListAsync(userId, CategoryKind.Expense, true, cancellationToken);

        var spentLookup = spentByCategory.ToDictionary(total => total.CategoryId, total => total.Amount);
        var categoryLookup = allCategories.ToDictionary(category => category.Id);

        var items = new List<BudgetItemDto>(limits.Count);

        foreach (var limit in limits)
        {
            if (!categoryLookup.TryGetValue(limit.CategoryId, out var category))
            {
                continue;
            }

            var spent = spentLookup.GetValueOrDefault(limit.CategoryId);
            items.Add(BuildItem(category.Id, category.Name, category.Color, limit.MonthlyLimit, spent));
        }

        items = [.. items.OrderByDescending(item => item.Percent)];

        var summary = new BudgetSummaryDto(
            Money.Round(items.Sum(item => item.Spent)),
            Money.Round(items.Sum(item => item.Limit)),
            items.Count(item => item.Status == BudgetStatus.Warning),
            items.Count(item => item.Status == BudgetStatus.Over));

        return new BudgetOverviewDto(month, summary, items);
    }

    public async Task<BudgetItemDto> SetLimitAsync(
        Guid userId,
        Guid categoryId,
        decimal monthlyLimit,
        CancellationToken cancellationToken = default)
    {
        var limit = Money.Round(monthlyLimit);

        if (limit <= 0m)
        {
            throw DomainException.Unprocessable("O limite deve ser maior que zero.", "invalid-limit");
        }

        var expenseCategories = await categories.ListAsync(userId, CategoryKind.Expense, true, cancellationToken);
        var category = expenseCategories.FirstOrDefault(candidate => candidate.Id == categoryId)
            ?? throw DomainException.Unprocessable(
                "Orcamento so existe para categoria de despesa.",
                "category-not-found");

        var existing = await context.Budgets.FirstOrDefaultAsync(
            budget => budget.UserId == userId && budget.CategoryId == categoryId,
            cancellationToken);

        if (existing is null)
        {
            context.Budgets.Add(new BudgetLimit
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                CategoryId = categoryId,
                MonthlyLimit = limit,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow,
            });
        }
        else
        {
            existing.MonthlyLimit = limit;
            existing.UpdatedAt = clock.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        var month = $"{clock.Today.Year:D4}-{clock.Today.Month:D2}";
        var spent = (await transactionsQuery.SumExpensesByCategoryAsync(userId, month, cancellationToken))
            .FirstOrDefault(total => total.CategoryId == categoryId)?.Amount ?? 0m;

        return BuildItem(category.Id, category.Name, category.Color, limit, spent);
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        var affected = await context.Budgets
            .Where(budget => budget.UserId == userId && budget.CategoryId == categoryId)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0;
    }

    private static BudgetItemDto BuildItem(Guid categoryId, string name, string color, decimal limit, decimal spent)
    {
        var percent = limit == 0m ? 0m : Math.Round(spent / limit * 100m, 1, MidpointRounding.AwayFromZero);

        var status = percent > 100m
            ? BudgetStatus.Over
            : percent >= WarningThreshold
                ? BudgetStatus.Warning
                : BudgetStatus.Normal;

        return new BudgetItemDto(
            categoryId,
            name,
            color,
            Money.Round(limit),
            Money.Round(spent),
            percent,
            status,
            Money.Round(limit - spent));
    }
}

public static class BudgetModuleExtensions
{
    public static IServiceCollection AddBudgetModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BudgetDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BudgetDbContext.Schema)));

        services.AddScoped<IBudgetService, BudgetService>();

        return services;
    }
}
