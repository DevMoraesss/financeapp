using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Shared;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Accounts;

internal sealed class AccountsService(AccountsDbContext context, IClock clock) : IAccountsService
{
    public async Task<AccountSummary> CreateAsync(
        Guid userId,
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = RequireName(request.Name);
        await EnsureUniqueNameAsync(userId, name, exceptId: null, cancellationToken);

        ValidateCycle(request.Type, request.ClosingDay, request.DueDay);

        var account = new Account
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = name,
            Type = request.Type,
            InitialBalance = Money.Round(request.InitialBalance),
            ClosingDay = request.Type == AccountType.CreditCard ? request.ClosingDay : null,
            DueDay = request.Type == AccountType.CreditCard ? request.DueDay : null,
            Archived = false,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        context.Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);

        return ToSummary(account);
    }

    public async Task<AccountSummary?> UpdateAsync(
        Guid userId,
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await context.Accounts
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Id == accountId, cancellationToken);

        if (account is null)
        {
            return null;
        }

        ValidateCycle(account.Type, request.ClosingDay, request.DueDay);

        var name = RequireName(request.Name);
        await EnsureUniqueNameAsync(userId, name, exceptId: accountId, cancellationToken);

        account.Name = name;
        account.ClosingDay = account.Type == AccountType.CreditCard ? request.ClosingDay : null;
        account.DueDay = account.Type == AccountType.CreditCard ? request.DueDay : null;
        account.UpdatedAt = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return ToSummary(account);
    }

    public async Task<bool> ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var affected = await context.Accounts
            .Where(account => account.UserId == userId && account.Id == accountId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(account => account.Archived, true)
                    .SetProperty(account => account.UpdatedAt, clock.UtcNow),
                cancellationToken);

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var affected = await context.Accounts
            .Where(account => account.UserId == userId && account.Id == accountId)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0;
    }

    /// <summary>Nome obrigatorio e dentro do tamanho da coluna (varchar 60).</summary>
    private static string RequireName(string? value)
    {
        var name = (value ?? string.Empty).Trim();

        if (name.Length is 0 or > 60)
        {
            throw DomainException.Unprocessable("O nome da conta e obrigatorio e tem ate 60 caracteres.", "invalid-name");
        }

        return name;
    }

    /// <summary>
    /// Mesmo nome so convive com conta arquivada. O indice unico do banco garante isso de
    /// qualquer jeito; checar antes e para devolver 409 com mensagem, e nao um 500.
    /// </summary>
    private async Task EnsureUniqueNameAsync(Guid userId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        // ILike do Postgres: comparacao sem diferenciar maiusculas, resolvida no banco. Usar
        // ToLower() em C# dentro de uma query do EF depende da cultura da maquina.
        var duplicated = await context.Accounts.AnyAsync(
            account => account.UserId == userId
                && account.Id != exceptId
                && !account.Archived
                && EF.Functions.ILike(account.Name, name),
            cancellationToken);

        if (duplicated)
        {
            throw DomainException.Conflict($"Ja existe uma conta chamada \"{name}\".", "duplicate-account");
        }
    }

    /// <summary>
    /// Cartao exige ciclo; os demais tipos nao podem ter. A mesma regra existe como CHECK no
    /// banco: validar aqui e para dar mensagem boa, nao para garantir a integridade.
    /// </summary>
    private static void ValidateCycle(AccountType type, short? closingDay, short? dueDay)
    {
        if (type != AccountType.CreditCard)
        {
            return;
        }

        if (closingDay is not { } closing || dueDay is not { } due)
        {
            throw DomainException.Unprocessable(
                "Cartao de credito precisa do dia de fechamento e do dia de vencimento.",
                "missing-card-cycle");
        }

        if (closing is < 1 or > 28 || due is < 1 or > 28)
        {
            throw DomainException.Unprocessable(
                "Os dias de fechamento e vencimento precisam estar entre 1 e 28.",
                "invalid-card-cycle");
        }

        if (closing == due)
        {
            throw DomainException.Unprocessable(
                "Fechamento e vencimento nao podem cair no mesmo dia: o ciclo fica ambiguo.",
                "ambiguous-card-cycle");
        }
    }

    private static AccountSummary ToSummary(Account account) => new(
        account.Id,
        account.Name,
        account.Type,
        account.InitialBalance,
        account.ClosingDay,
        account.DueDay,
        account.Archived);
}
