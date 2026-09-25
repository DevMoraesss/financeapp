using FinanceMove.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceMove.Modules.Identity;

/// <summary>
/// Ponto unico de registro do modulo Identidade na injecao de dependencia.
/// O Program.cs chama AddIdentityModule e nao conhece nada de dentro do modulo.
/// </summary>
public static class IdentityModuleExtensions
{
    /// <summary>
    /// Tamanho minimo da senha. Comprimento protege mais que simbolo obrigatorio: o NIST
    /// (SP 800-63B) pede 15 para senha como fator unico; 12 e o meio-termo para uma frase facil
    /// de lembrar, com lockout e rate limit cobrindo o resto.
    /// </summary>
    public const int MinPasswordLength = 12;

    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<IdentityModuleDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                // Cada modulo tem a SUA tabela de historico de migrations, dentro do SEU schema:
                // e isso que permite migrar modulos de forma independente.
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityModuleDbContext.Schema)));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<RegistrationOptions>(configuration.GetSection(RegistrationOptions.SectionName));

        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Regra de senha: comprimento vale mais que simbolos obrigatorios, que so levam
                // o usuario a inventar "Senha@123".
                options.Password.RequiredLength = MinPasswordLength;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // Protecao contra forca bruta (SPEC US-01).
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<IdentityModuleDbContext>();

        // AddDefaultTokenProviders() fica de fora por enquanto: ele so serve para os tokens de
        // confirmacao de e-mail e de reset de senha, que dependem do envio de e-mail (Resend) e
        // ainda nao foram implementados. Entra junto com esse fluxo.

        services.AddScoped<IIdentityQuery, IdentityQuery>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
