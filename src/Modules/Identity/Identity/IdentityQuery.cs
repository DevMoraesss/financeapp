using FinanceMove.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Identity;

/// <summary>
/// Implementação do contrato público do módulo. É <c>internal</c> de propósito: os outros módulos
/// enxergam apenas a interface <see cref="IIdentityQuery"/>, nunca esta classe nem o DbContext.
/// </summary>
internal sealed class IdentityQuery(IdentityModuleDbContext context) : IIdentityQuery
{
    public async Task<UserDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserDto(user.Id, user.Name, user.Email!))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken);
}
