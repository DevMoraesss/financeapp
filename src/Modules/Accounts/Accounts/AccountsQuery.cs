using FinanceMove.Modules.Accounts.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Accounts;

/// <summary>
/// Implementação <c>internal</c> do contrato público: os outros módulos só enxergam
/// <see cref="IAccountsQuery"/>.
/// </summary>
internal sealed class AccountsQuery(AccountsDbContext context) : IAccountsQuery
{
    public async Task<AccountSummary?> FindAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        await context.Accounts
            .AsNoTracking()
            .Where(account => account.UserId == userId && account.Id == accountId)
            .Select(account => ToSummary(account))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(
        Guid userId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        await context.Accounts
            .AsNoTracking()
            .Where(account => account.UserId == userId && (includeArchived || !account.Archived))
            .OrderBy(account => account.Name)
            .Select(account => ToSummary(account))
            .ToListAsync(cancellationToken);

    public Task<bool> IsUsableAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        context.Accounts
            .AsNoTracking()
            .AnyAsync(
                account => account.UserId == userId && account.Id == accountId && !account.Archived,
                cancellationToken);

    private static AccountSummary ToSummary(Account account) => new(
        account.Id,
        account.Name,
        account.Type,
        account.InitialBalance,
        account.ClosingDay,
        account.DueDay,
        account.Archived,
        account.CreditLimit);
}
