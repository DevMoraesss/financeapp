using System.Text.RegularExpressions;
using FinanceMove.Modules.Identity.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using FinanceMove.Shared.Events;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Transactions;

internal sealed partial class CategoryService(TransactionsDbContext context, IClock clock) : ICategoryService
{
    /// <summary>Nome reservado das categorias de sistema usadas pelo ajuste de saldo.</summary>
    internal const string AdjustmentName = "Ajuste";

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(
        Guid userId,
        CategoryKind? type = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        await context.Categories
            .AsNoTracking()
            .Where(category => category.UserId == userId)
            .Where(category => type == null || category.Type == type)
            .Where(category => includeArchived || !category.Archived)
            .OrderBy(category => category.Type)
            .ThenBy(category => category.Name)
            .Select(category => Mapping.ToDto(category))
            .ToListAsync(cancellationToken);

    public async Task<CategoryDto> CreateAsync(
        Guid userId,
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var (name, color, icon) = RequireAppearance(request.Name, request.Color, request.Icon);
        await EnsureUniqueNameAsync(userId, name, request.Type, exceptId: null, cancellationToken);

        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = name,
            Type = request.Type,
            Color = color,
            Icon = icon,
            System = false,
            Archived = false,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken);

        return Mapping.ToDto(category);
    }

    public async Task<CategoryDto?> UpdateAsync(
        Guid userId,
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await context.Categories
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return null;
        }

        if (category.System)
        {
            throw DomainException.Unprocessable(
                "A categoria \"Ajuste\" e do sistema e nao pode ser editada.",
                "system-category");
        }

        var (name, color, icon) = RequireAppearance(request.Name, request.Color, request.Icon);
        await EnsureUniqueNameAsync(userId, name, category.Type, exceptId: categoryId, cancellationToken);

        category.Name = name;
        category.Color = color;
        category.Icon = icon;
        category.UpdatedAt = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Mapping.ToDto(category);
    }

    public async Task<bool> ArchiveAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Id == categoryId, cancellationToken);

        if (category is null)
        {
            return false;
        }

        if (category.System)
        {
            throw DomainException.Unprocessable(
                "A categoria \"Ajuste\" e do sistema e nao pode ser arquivada.",
                "system-category");
        }

        // Arquiva em vez de excluir: as transacoes antigas continuam mostrando a categoria certa
        // nos relatorios (modelo-de-dados secao 5.3).
        category.Archived = true;
        category.UpdatedAt = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Nome, cor e icone dentro do que o banco e a tela aceitam. A cor vai parar num atributo de
    /// estilo no front, entao so passa hexadecimal puro, nunca texto livre.
    /// </summary>
    private static (string Name, string Color, string Icon) RequireAppearance(string? name, string? color, string? icon)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length is 0 or > 40)
        {
            throw DomainException.Unprocessable("O nome da categoria e obrigatorio e tem ate 40 caracteres.", "invalid-name");
        }

        if (color is null || !HexColor().IsMatch(color))
        {
            throw DomainException.Unprocessable("Cor invalida. Use o formato #RRGGBB.", "invalid-color");
        }

        if (icon is null || !IconName().IsMatch(icon))
        {
            throw DomainException.Unprocessable("Icone invalido.", "invalid-icon");
        }

        return (trimmed, color.ToLowerInvariant(), icon);
    }

    private async Task EnsureUniqueNameAsync(
        Guid userId,
        string name,
        CategoryKind type,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        // ILike do Postgres: comparacao sem diferenciar maiusculas, resolvida no banco.
        var duplicated = await context.Categories.AnyAsync(
            category => category.UserId == userId
                && category.Id != exceptId
                && !category.Archived
                && category.Type == type
                && EF.Functions.ILike(category.Name, name),
            cancellationToken);

        if (duplicated)
        {
            throw DomainException.Conflict($"Ja existe uma categoria chamada \"{name}\".", "duplicate-category");
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex IconName();
}

/// <summary>
/// Cria as categorias padrao quando um usuario se cadastra (SPEC Apendice A).
/// </summary>
/// <remarks>
/// Esta classe e a fronteira do monolito modular funcionando na pratica: o modulo Identidade
/// publica UserRegistered e nao faz ideia de que categorias existem. Se um dia Transactions for
/// removido do sistema, o cadastro continua funcionando.
/// </remarks>
internal sealed class CategorySeedHandler(TransactionsDbContext context, IClock clock)
    : IEventHandler<UserRegistered>
{
    /// <summary>Nome, cor e icone das categorias criadas no cadastro. Nomes em pt-BR: sao dados do usuario.</summary>
    private static readonly (string Name, CategoryKind Type, string Color, string Icon, bool System)[] Seed =
    [
        ("Mercado", CategoryKind.Expense, "#f59e0b", "shopping-cart", false),
        ("Alimentacao", CategoryKind.Expense, "#fb5d6d", "utensils", false),
        ("Moradia", CategoryKind.Expense, "#38bdf8", "home", false),
        ("Transporte", CategoryKind.Expense, "#a78bfa", "car", false),
        ("Saude", CategoryKind.Expense, "#22d3ee", "heart-pulse", false),
        ("Academia", CategoryKind.Expense, "#34d399", "dumbbell", false),
        ("Assinaturas", CategoryKind.Expense, "#f472b6", "wifi", false),
        ("Lazer", CategoryKind.Expense, "#fb923c", "plane", false),
        ("Presentes", CategoryKind.Expense, "#e879f9", "gift", false),
        ("Educacao", CategoryKind.Expense, "#60a5fa", "graduation-cap", false),
        ("Outros", CategoryKind.Expense, "#8b979f", "circle-dashed", false),

        ("Salario", CategoryKind.Income, "#2ee59d", "briefcase", false),
        ("Freelance", CategoryKind.Income, "#34d399", "laptop", false),
        ("Outros", CategoryKind.Income, "#8b979f", "circle-dashed", false),

        // Duas linhas "Ajuste", uma de cada tipo: o ajuste de saldo pode ser para cima ou para
        // baixo, e uma linha so nao consegue ser as duas coisas (decidido em 15/08/2026).
        (CategoryService.AdjustmentName, CategoryKind.Expense, "#6b7280", "wrench", true),
        (CategoryService.AdjustmentName, CategoryKind.Income, "#6b7280", "wrench", true),
    ];

    public async Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var categories = Seed.Select(item => new Category
        {
            Id = Guid.CreateVersion7(),
            UserId = domainEvent.UserId,
            Name = item.Name,
            Type = item.Type,
            Color = item.Color,
            Icon = item.Icon,
            System = item.System,
            Archived = false,
            CreatedAt = now,
            UpdatedAt = now,
        });

        context.Categories.AddRange(categories);
        await context.SaveChangesAsync(cancellationToken);
    }
}
