using Microsoft.AspNetCore.Identity;

namespace FinanceMove.Modules.Identity;

/// <summary>
/// Usuário do FinanceMove. Herda de <see cref="IdentityUser{TKey}"/> para ganhar de graça o que
/// nunca se deve escrever à mão em app financeiro: hash PBKDF2 da senha, lockout por tentativas,
/// tokens de confirmação de e-mail e de reset (ADR-001, Decisão 5).
/// </summary>
/// <remarks>
/// A tabela vira <c>identity.app_user</c> - e não <c>user</c> - porque <c>user</c> é palavra
/// reservada no PostgreSQL e exigiria aspas em toda query manual.
/// </remarks>
public sealed class AppUser : IdentityUser<Guid>
{
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
