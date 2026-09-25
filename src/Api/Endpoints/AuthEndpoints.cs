using FinanceMove.Modules.Identity.Contracts;
using FinanceMove.Shared;
using Microsoft.AspNetCore.Mvc;

namespace FinanceMove.Api.Endpoints;

/// <summary>Cadastro, login, refresh e logout (docs/api.md secao 2).</summary>
internal static class AuthEndpoints
{
    internal const string RefreshCookie = "refreshToken";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth").AllowAnonymous();

        group.MapPost("/register", async (
            RegisterRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            var user = await auth.RegisterAsync(request, cancellationToken);
            return Results.Created($"/api/v1/users/{user.Id}", user);
        }).RequireRateLimiting(RateLimiting.AuthPolicy);

        group.MapPost("/login", async (
            LoginRequest request,
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.LoginAsync(request, cancellationToken);

            if (result is null)
            {
                // Mensagem identica para senha errada e conta bloqueada, de proposito: dizer qual
                // dos dois foi entregaria informacao a quem esta tentando adivinhar.
                return Unauthorized("E-mail ou senha invalidos.");
            }

            SetRefreshCookie(http, result);
            return Results.Ok(ToResponse(result));
        }).RequireRateLimiting(RateLimiting.AuthPolicy);

        group.MapPost("/refresh", async (
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var token = http.Request.Cookies[RefreshCookie];

            if (string.IsNullOrWhiteSpace(token))
            {
                return Unauthorized("Sessao expirada. Entre novamente.");
            }

            var result = await auth.RefreshAsync(token, cancellationToken);

            if (result is null)
            {
                DeleteRefreshCookie(http);
                return Unauthorized("Sessao expirada. Entre novamente.");
            }

            SetRefreshCookie(http, result);
            return Results.Ok(ToResponse(result));
        }).RequireRateLimiting(RateLimiting.SessionPolicy);

        group.MapPost("/logout", async (
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var token = http.Request.Cookies[RefreshCookie];

            if (!string.IsNullOrWhiteSpace(token))
            {
                await auth.LogoutAsync(token, cancellationToken);
            }

            DeleteRefreshCookie(http);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimiting.SessionPolicy);
    }

    /// <summary>
    /// O refresh token vai em cookie httpOnly: o JavaScript da pagina nao consegue ler, entao um
    /// XSS nao rouba a sessao longa. So o access token (curto) chega ao front, e ele fica em
    /// memoria (ADR-001, Decisao 5).
    /// </summary>
    private static void SetRefreshCookie(HttpContext http, AuthResult result)
    {
        var options = RefreshCookieOptions(http);
        options.Expires = result.RefreshExpiresAt;
        http.Response.Cookies.Append(RefreshCookie, result.RefreshToken, options);
    }

    /// <summary>
    /// Apagar cookie exige repetir o mesmo Path com que ele foi criado. Sem isso o navegador
    /// entende que e outro cookie e mantem o original.
    /// </summary>
    internal static void DeleteRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshCookie, RefreshCookieOptions(http));

    /// <summary>
    /// SameSite=Strict porque a SPA e a API respondem na MESMA origem: a Vercel repassa /api para
    /// o Railway (docs/ADR-002). O navegador nunca manda este cookie em requisicao vinda de outro
    /// site, o que fecha a porta para CSRF no refresh e no logout. Fora de desenvolvimento o
    /// cookie e sempre Secure, mesmo que o proxy esqueca de avisar que a conexao era HTTPS.
    /// </summary>
    private static CookieOptions RefreshCookieOptions(HttpContext http) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps
            || !http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        IsEssential = true,
    };

    private static object ToResponse(AuthResult result) => new
    {
        accessToken = result.AccessToken,
        expiresAt = result.ExpiresAt,
        user = result.User,
    };

    private static IResult Unauthorized(string detail) => Results.Problem(new ProblemDetails
    {
        Type = "https://financemove.app/errors/invalid-credentials",
        Title = "Nao autorizado",
        Status = StatusCodes.Status401Unauthorized,
        Detail = detail,
    });
}
