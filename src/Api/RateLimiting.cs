using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FinanceMove.Api;

/// <summary>Limites por IP, em requisicoes por minuto. Configuraveis em RateLimiting:*.</summary>
internal sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Teto geral de qualquer rota. Folgado para gente, curto para robo.</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>Login, cadastro, troca de senha e exclusao de conta: as portas de forca bruta.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>
    /// Renovacao de sessao. Mais folgado que o de login porque cada F5 renova, e uma familia
    /// inteira atras do mesmo roteador divide o mesmo IP.
    /// </summary>
    public int SessionPerMinute { get; set; } = 30;
}

/// <summary>
/// Rate limiting da API (docs/api.md secao 3, codigo 429).
/// </summary>
/// <remarks>
/// O lockout do ASP.NET Identity (5 erros, 15 minutos) protege UMA conta de forca bruta. O limite
/// por IP protege o resto: robo testando muitos e-mails, cadastro em massa e chute de codigo de
/// convite. As duas trancas se somam.
/// </remarks>
internal static class RateLimiting
{
    public const string AuthPolicy = "auth";
    public const string SessionPolicy = "session";

    public static IServiceCollection AddFinanceMoveRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Lido via IOptions na hora da requisicao, e nao aqui: assim o valor vem da configuracao
        // final do host (inclusive a dos testes), e nao de um retrato tirado antes do Build.
        services.Configure<RateLimitSettings>(configuration.GetSection(RateLimitSettings.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                PerMinute($"global:{ClientKey(http)}", Settings(http).GlobalPerMinute));

            options.AddPolicy(AuthPolicy, http => PerMinute($"auth:{ClientKey(http)}", Settings(http).AuthPerMinute));
            options.AddPolicy(SessionPolicy, http => PerMinute($"session:{ClientKey(http)}", Settings(http).SessionPerMinute));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Type = "https://financemove.app/errors/rate-limit",
                    Title = "Muitas tentativas",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = "Voce fez muitas tentativas em pouco tempo. Espere um minuto e tente de novo.",
                    Instance = http.Request.Path,
                };

                problem.Extensions["traceId"] = http.TraceIdentifier;
                await http.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitSettings Settings(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;

    private static RateLimitPartition<string> PerMinute(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(permits, 1),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    /// <summary>
    /// IP do cliente, ja resolvido pelo UseForwardedHeaders (Program.cs) a partir do
    /// X-Forwarded-For que a Vercel e o Railway preenchem.
    /// </summary>
    private static string ClientKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
}
