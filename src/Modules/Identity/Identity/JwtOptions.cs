namespace FinanceMove.Modules.Identity;

/// <summary>
/// Configuracao do token de acesso. Vem de appsettings (dev) ou de variavel de ambiente
/// (Railway). A chave NUNCA e comitada.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Chave simetrica de assinatura. Minimo de 32 bytes, gerada aleatoriamente.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "financemove";

    public string Audience { get; set; } = "financemove-spa";

    /// <summary>
    /// Vida do access token. Curta de proposito: como nao ha revogacao de access token, o que
    /// limita o estrago de um token vazado e ele expirar rapido. Quem mantem a sessao viva e o
    /// refresh token, esse sim revogavel.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;
}
