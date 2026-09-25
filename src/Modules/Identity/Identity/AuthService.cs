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

/// <summary>Codigos HTTP usados pelo modulo, sem puxar a dependencia do ASP.NET para ca.</summary>
file static class StatusCodes
{
    public const int Forbidden = 403;
}

internal sealed class AuthService(
    UserManager<AppUser> userManager,
    IdentityModuleDbContext context,
    IOptions<JwtOptions> jwtOptions,
    IOptions<RegistrationOptions> registrationOptions,
    IEventBus eventBus,
    IClock clock) : IAuthService
{
    /// <summary>
    /// Teto de tamanho da senha. O hash PBKDF2 custa caro de proposito; sem teto, uma senha de
    /// megabytes viraria um jeito barato de ocupar a CPU do servidor.
    /// </summary>
    internal const int MaxPasswordLength = 128;

    /// <summary>
    /// Janela em que um refresh token recem-rotacionado ainda pode ser trocado de novo.
    /// </summary>
    /// <remarks>
    /// Existe por causa de uma corrida benigna e comum: duas abas abertas, ou tres requisicoes da
    /// mesma tela recebendo 401 juntas, renovam a sessao com o MESMO cookie em milissegundos de
    /// diferenca. Sem a janela, a segunda renovacao parecia roubo de token e derrubava a sessao
    /// inteira, e o usuario era deslogado a cada 15 minutos. Fora da janela, reuso continua
    /// sendo tratado como roubo.
    /// </remarks>
    internal static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly RegistrationOptions _registration = registrationOptions.Value;

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Convite primeiro, antes de qualquer consulta: sem o codigo certo, a resposta e sempre a
        // mesma e nao revela nada sobre quem ja tem conta.
        EnsureInvited(request.InviteCode);

        var name = (request.Name ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (name.Length is 0 or > 120)
        {
            throw DomainException.Unprocessable("Informe seu nome (ate 120 caracteres).", "invalid-name");
        }

        if (email.Length is < 3 or > 254)
        {
            throw DomainException.Unprocessable("E-mail invalido.", "invalid-email");
        }

        EnsurePasswordLength(password);

        var existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            // Resposta identica a de sucesso, de proposito: ver docs/api.md secao 2.1.
            // Quem ja tem conta descobre pelo e-mail, nao pela resposta HTTP.
            return new UserDto(existing.Id, existing.Name, existing.Email!);
        }

        if (_registration.MaxUsers > 0
            && await context.Users.CountAsync(cancellationToken) >= _registration.MaxUsers)
        {
            throw new DomainException(
                "O limite de usuarios deste FinanceMove foi atingido.",
                StatusCodes.Forbidden,
                "user-limit-reached");
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Email = email,
            UserName = email,
            CreatedAt = clock.UtcNow,
        };

        var result = await userManager.CreateAsync(user, password);

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
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrEmpty(request.Password)
            || request.Password.Length > MaxPasswordLength)
        {
            return null;
        }

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
        var stored = await FindTokenAsync(token => token.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        if (stored.RevokedAt is null)
        {
            // Revogacao CONDICIONAL: se duas requisicoes chegarem juntas, so uma consegue
            // rotacionar. O id do sucessor ja vai gravado na mesma escrita, para a perdedora
            // enxergar que foi uma rotacao (e nao um logout) ao reler o token.
            var successorId = Guid.CreateVersion7();
            var now = clock.UtcNow;

            var rotated = await context.RefreshTokens
                .Where(token => token.Id == stored.Id && token.RevokedAt == null)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(token => token.RevokedAt, now)
                        .SetProperty(token => token.ReplacedBy, successorId),
                    cancellationToken);

            if (rotated == 1)
            {
                return await IssueForAsync(stored.UserId, successorId, cancellationToken);
            }

            // Perdeu a corrida: outra requisicao acabou de rotacionar este token. Rele e segue
            // para a regra da janela de tolerancia logo abaixo.
            stored = await FindTokenAsync(token => token.Id == stored.Id, cancellationToken);

            if (stored?.RevokedAt is null)
            {
                return null;
            }
        }

        var reusedAt = clock.UtcNow;

        // So token ROTACIONADO (com sucessor) ganha tolerancia. Token revogado por logout ou por
        // troca de senha nao tem sucessor e morre na hora.
        if (stored.ReplacedBy is not null && reusedAt - stored.RevokedAt.Value <= ReuseGrace)
        {
            return await IssueForAsync(stored.UserId, Guid.CreateVersion7(), cancellationToken);
        }

        // Token ja usado sendo apresentado de novo, fora da janela: sinal classico de roubo.
        // Derruba todas as sessoes do usuario, obrigando novo login em todo lugar.
        await RevokeAllSessionsAsync(stored.UserId, cancellationToken);
        return null;
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
        if (string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            return false;
        }

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
        EnsurePasswordLength(newPassword ?? string.Empty);

        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null || string.IsNullOrEmpty(currentPassword) || currentPassword.Length > MaxPasswordLength)
        {
            return false;
        }

        if (!await userManager.CheckPasswordAsync(user, currentPassword))
        {
            return false;
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword!);

        if (!result.Succeeded)
        {
            var reasons = string.Join(' ', result.Errors.Select(TranslateIdentityError));
            throw DomainException.Unprocessable(reasons, "invalid-password");
        }

        // Trocar a senha derruba todas as sessoes abertas, em todos os dispositivos.
        await RevokeAllSessionsAsync(userId, cancellationToken);
        return true;
    }

    private async Task<AuthResult?> IssueForAsync(Guid userId, Guid refreshTokenId, CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        return user is null ? null : await IssueAsync(user, cancellationToken, refreshTokenId);
    }

    private async Task<AuthResult> IssueAsync(
        AppUser user,
        CancellationToken cancellationToken,
        Guid? refreshTokenId = null)
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
            Id = refreshTokenId ?? Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = Hash(rawRefresh),
            ExpiresAt = refreshExpires,
            CreatedAt = now,
        };

        context.RefreshTokens.Add(refresh);
        await context.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            accessExpires,
            rawRefresh,
            refreshExpires,
            new UserDto(user.Id, user.Name, user.Email!));
    }

    private Task<RefreshToken?> FindTokenAsync(
        System.Linq.Expressions.Expression<Func<RefreshToken, bool>> predicate,
        CancellationToken cancellationToken) =>
        context.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken);

    /// <summary>
    /// Mata todas as sessoes do usuario, inclusive tokens rotacionados que ainda estariam dentro
    /// da janela de tolerancia: vencer o <c>ExpiresAt</c> e o que fecha essa porta.
    /// </summary>
    private async Task RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        await context.RefreshTokens
            .Where(token => token.UserId == userId && token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(token => token.ExpiresAt, now)
                    .SetProperty(token => token.RevokedAt, token => token.RevokedAt ?? now),
                cancellationToken);
    }

    /// <summary>
    /// Confere o codigo de convite em tempo constante: comparar string com == termina no primeiro
    /// caractere diferente, e o tempo de resposta ajudaria a adivinhar o codigo aos poucos.
    /// </summary>
    /// <remarks>
    /// Os dois lados passam pela mesma normalizacao (sem espacos nas pontas, sem diferenca de
    /// maiuscula): um espaco colado junto no painel do Railway, ou o celular pondo a primeira letra
    /// em maiuscula, nao podem barrar um convidado legitimo. O codigo e hexadecimal, entao ignorar
    /// maiuscula nao tira seguranca nenhuma.
    /// </remarks>
    private void EnsureInvited(string? informed)
    {
        // Espaco em branco configurado conta como "sem codigo" (cadastro fechado). Se contasse como
        // codigo, viraria "" depois da normalizacao e um convite vazio passaria.
        if (string.IsNullOrWhiteSpace(_registration.InviteCode))
        {
            if (_registration.Open)
            {
                return;
            }

            throw new DomainException(
                "O cadastro esta fechado. Peca um convite a quem administra o FinanceMove.",
                StatusCodes.Forbidden,
                "registration-closed");
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeInvite(_registration.InviteCode)));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeInvite(informed)));

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new DomainException("Codigo de convite invalido.", StatusCodes.Forbidden, "invalid-invite");
        }
    }

    private static string NormalizeInvite(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static void EnsurePasswordLength(string password)
    {
        if (password.Length > MaxPasswordLength)
        {
            throw DomainException.Unprocessable(
                $"A senha pode ter no maximo {MaxPasswordLength} caracteres.",
                "password-too-long");
        }
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
        "PasswordTooShort" => $"A senha precisa ter pelo menos {IdentityModuleExtensions.MinPasswordLength} caracteres.",
        "PasswordRequiresDigit" => "A senha precisa ter ao menos um numero.",
        "PasswordRequiresUpper" => "A senha precisa ter ao menos uma letra maiuscula.",
        "PasswordRequiresLower" => "A senha precisa ter ao menos uma letra minuscula.",
        "PasswordRequiresNonAlphanumeric" => "A senha precisa ter ao menos um simbolo.",
        "DuplicateUserName" or "DuplicateEmail" => "Este e-mail ja esta em uso.",
        "InvalidEmail" => "E-mail invalido.",
        _ => "Nao foi possivel criar a conta com estes dados.",
    };
}
