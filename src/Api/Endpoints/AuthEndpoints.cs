using FinanceMove.Modules.Identity.Contracts;
using FinanceMove.Shared;
using Microsoft.AspNetCore.Mvc;

namespace FinanceMove.Api.Endpoints;

/// <summary>Cadastro, login, refresh e logout (docs/api.md secao 2).</summary>
internal static class AuthEndpoints
{
    private const string RefreshCookie = "refreshToken";

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
        });

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
        });

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
                http.Response.Cookies.Delete(RefreshCookie);
                return Unauthorized("Sessao expirada. Entre novamente.");
            }

            SetRefreshCookie(http, result);
            return Results.Ok(ToResponse(result));
        });

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

            http.Response.Cookies.Delete(RefreshCookie);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// O refresh token vai em cookie httpOnly: o JavaScript da pagina nao consegue ler, entao um
    /// XSS nao rouba a sessao longa. So o access token (curto) chega ao front, e ele fica em
    /// memoria (ADR-001, Decisao 5).
    /// </summary>
    private static void SetRefreshCookie(HttpContext http, AuthResult result) =>
        http.Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = http.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = result.RefreshExpiresAt,
            Path = "/api/v1/auth",
            IsEssential = true,
        });

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
