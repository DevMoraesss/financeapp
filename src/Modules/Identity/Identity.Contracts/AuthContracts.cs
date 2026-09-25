namespace FinanceMove.Modules.Identity.Contracts;

/// <summary>Entrada do cadastro (docs/api.md secao 2).</summary>
/// <param name="InviteCode">Obrigatorio quando o servidor tem codigo de convite configurado.</param>
public sealed record RegisterRequest(string Name, string Email, string Password, string? InviteCode = null);

/// <summary>Entrada do login.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// Resultado de login ou refresh.
/// </summary>
/// <remarks>
/// O <paramref name="RefreshToken"/> nao vai no corpo da resposta HTTP: o host o coloca num
/// cookie httpOnly, que o JavaScript nao consegue ler. So o access token chega ao front, e ele
/// fica em memoria (ADR-001, Decisao 5).
/// </remarks>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    UserDto User);

/// <summary>Servico de autenticacao exposto pelo modulo Identidade.</summary>
public interface IAuthService
{
    /// <summary>
    /// Cria a conta e dispara o evento <see cref="UserRegistered"/>.
    /// </summary>
    /// <remarks>
    /// Se o e-mail ja existir, NAO lanca erro: devolve o mesmo resultado de sucesso sem criar
    /// nada. Responder diferente permitiria a qualquer um descobrir quem tem conta no app
    /// (user enumeration). Ver docs/api.md secao 2.1.
    /// <para>
    /// O codigo de convite e conferido ANTES de olhar o e-mail: sem o codigo certo a resposta e
    /// sempre a mesma, entao quem nao foi convidado nao aprende nada sobre quem tem conta.
    /// </para>
    /// </remarks>
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Devolve nulo quando a credencial esta errada ou a conta esta bloqueada.</summary>
    Task<AuthResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Rotaciona o refresh token. Devolve nulo se o token for invalido, expirado ou ja usado.</summary>
    Task<AuthResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard delete do usuario (LGPD, SPEC US-13). Cascateia para todos os schemas por causa da
    /// chave estrangeira em user_id (modelo-de-dados secao 1.1).
    /// </summary>
    Task<bool> DeleteAccountAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}
