using System.Security.Claims;
using FinanceMove.Shared;

namespace FinanceMove.Api;

/// <summary>
/// Resolve o usuário da requisição a partir do <b>token</b> - nunca de rota, query ou corpo.
/// </summary>
/// <remarks>
/// É aqui que o isolamento multi-tenant começa (SPEC US-14). Se um dia alguém tentar "facilitar"
/// aceitando um <c>userId</c> vindo do cliente, todo o modelo de segurança cai junto.
/// <para>A configuração de autenticação em si entra na sessão de features de auth (F4a).</para>
/// </remarks>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("Requisição sem usuário autenticado.");

    public bool IsAuthenticated =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out _);
}
