using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinanceMove.Modules.Identity.Contracts;
using FinanceMove.Shared;
using FinanceMove.Shared.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinanceMove.Modules.Identity;

internal sealed class AuthService(
    UserManager<AppUser> userManager,
    IdentityModuleDbContext context,
    IOptions<JwtOptions> jwtOptions,
    IEventBus eventBus,
    IClock clock) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        var existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            // Resposta identica a de sucesso, de proposito: ver docs/api.md secao 2.1.
            // Quem ja tem conta descobre pelo e-mail, nao pela resposta HTTP.
            return new UserDto(existing.Id, existing.Name, existing.Email!);
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            Email = email,
            UserName = email,
            CreatedAt = clock.UtcNow,
        };

        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            var reasons = string.Join(' ', result.Errors.Select(TranslateIdentityError));
            throw DomainException.Unprocessable(reasons, "invalid-credentials");
        }

        // O seed de categorias acontece aqui dentro, no ouvinte do modulo Transactions.
        // Rodando na mesma transacao de banco, nunca existe usuario sem categorias.
        await eventBus.PublishAsync(new UserRegistered(user.Id, email, clock.UtcNow), cancellationToken);

        return new UserDto(user.Id, user.Name, email);
    }

    public async Task<AuthResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        if (user is null)
        {
            return null;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Conta o erro para o lockout do Identity funcionar (SPEC US-01).
            await userManager.AccessFailedAsync(user);
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);

        return await IssueAsync(user, cancellationToken);
    }

    public async Task<AuthResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var stored = await context.RefreshTokens.FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (stored is null || stored.ExpiresAt <= clock.UtcNow)
        {
            return null;
        }

        if (stored.RevokedAt is not null)
        {
            // Token ja usado sendo apresentado de novo: sinal classico de roubo. Derruba a
            // cadeia inteira do usuario, obrigando novo login.
            await context.RefreshTokens
                .Where(token => token.UserId == stored.UserId && token.RevokedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, clock.UtcNow), cancellationToken);

            return null;
        }

        var user = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == stored.UserId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var issued = await IssueAsync(user, cancellationToken, replacing: stored);
        return issued;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);

        await context.RefreshTokens
            .Where(token => token.TokenHash == hash && token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, clock.UtcNow), cancellationToken);
    }

    public async Task<bool> DeleteAccountAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null || !await userManager.CheckPasswordAsync(user, password))
        {
            return false;
        }

        // Um DELETE so. As tabelas dos outros modulos caem por causa da chave estrangeira em
        // user_id com ON DELETE CASCADE (modelo-de-dados secao 1.1).
        await context.Users.Where(candidate => candidate.Id == userId).ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return false;
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);

        if (result.Succeeded)
        {
            // Trocar a senha derruba todas as sessoes abertas em outros dispositivos.
            await context.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, clock.UtcNow), cancellationToken);
        }

        return result.Succeeded;
    }

    private async Task<AuthResult> IssueAsync(
        AppUser user,
        CancellationToken cancellationToken,
        RefreshToken? replacing = null)
    {
        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);

        var accessToken = BuildAccessToken(user, accessExpires);

        // Token cru so existe aqui e na resposta. No banco fica apenas o hash: se o banco vazar,
        // os tokens vazados nao servem para nada.
        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var refresh = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = Hash(rawRefresh),
            ExpiresAt = refreshExpires,
            CreatedAt = now,
        };

        context.RefreshTokens.Add(refresh);

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedBy = refresh.Id;
        }

        await context.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            accessExpires,
            rawRefresh,
            refreshExpires,
            new UserDto(user.Id, user.Name, user.Email!));
    }

    private string BuildAccessToken(AppUser user, DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            // NameIdentifier e o que o ICurrentUser le. Toda query do sistema filtra por ele.
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        ];

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: clock.UtcNow.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string TranslateIdentityError(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "A senha precisa ter pelo menos 8 caracteres.",
        "PasswordRequiresDigit" => "A senha precisa ter ao menos um numero.",
        "PasswordRequiresUpper" => "A senha precisa ter ao menos uma letra maiuscula.",
        "PasswordRequiresLower" => "A senha precisa ter ao menos uma letra minuscula.",
        "PasswordRequiresNonAlphanumeric" => "A senha precisa ter ao menos um simbolo.",
        "DuplicateUserName" or "DuplicateEmail" => "Este e-mail ja esta em uso.",
        "InvalidEmail" => "E-mail invalido.",
        _ => "Nao foi possivel criar a conta com estes dados.",
    };
}
