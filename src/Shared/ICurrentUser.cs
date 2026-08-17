namespace FinanceMove.Shared;

/// <summary>
/// Quem é o usuário desta requisição - resolvido <b>sempre</b> a partir do token, nunca de um
/// parâmetro de rota ou corpo.
/// <para>
/// É a base do isolamento multi-tenant (SPEC US-14 / ADR-001 Decisão 2): todo módulo filtra suas
/// queries por <see cref="Id"/>, e recurso de outro usuário responde 404 - nunca 403, que
/// confirmaria a existência do registro.
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>Id do usuário autenticado. Lança se não houver sessão.</summary>
    Guid Id { get; }

    bool IsAuthenticated { get; }
}
