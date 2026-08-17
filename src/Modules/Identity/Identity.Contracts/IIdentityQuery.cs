namespace FinanceMove.Modules.Identity.Contracts;

/// <summary>
/// Contrato público do módulo Identidade - a ÚNICA porta de entrada para os outros módulos.
/// <para>
/// Nenhum módulo lê a tabela <c>identity.app_user</c> diretamente (ADR-001, Decisão 2 e
/// docs/arquitetura.md secao 2). Quem precisar de dado de usuário passa por aqui.
/// </para>
/// </summary>
public interface IIdentityQuery
{
    Task<UserDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default);
}
