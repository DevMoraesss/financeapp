namespace FinanceMove.Modules.Identity;

/// <summary>
/// Refresh token com rotação e revogação server-side (ADR-001, Decisão 5).
/// </summary>
/// <remarks>
/// Guardamos o <b>hash</b> do token, nunca o valor cru: se o banco vazar, os tokens vazados não
/// servem para nada - mesma lógica de por que não se guarda senha em texto puro.
/// <para>
/// Rotação: ao usar um refresh token, ele é revogado (<see cref="RevokedAt"/>) e
/// <see cref="ReplacedBy"/> aponta para o novo. Se um token já revogado for usado de novo, é sinal
/// de roubo - a cadeia inteira do usuário deve ser invalidada.
/// </para>
/// </remarks>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>SHA-256 do token entregue ao cliente (Base64, 44 caracteres).</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? ReplacedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
