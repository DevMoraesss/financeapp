using FinanceMove.Shared.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Identity;

/// <summary>
/// DbContext do módulo Identidade. Dono exclusivo do schema <c>identity</c>.
/// </summary>
/// <remarks>
/// Herda de <see cref="IdentityUserContext{TUser,TKey}"/> (e não de <c>IdentityDbContext</c>)
/// de propósito: a versão sem "Db" no nome <b>não cria as tabelas de papéis/roles</b>, que o
/// FinanceMove não usa. Tabela que não existe não precisa ser mantida nem auditada.
/// </remarks>
public sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    /// <summary>Schema do módulo no Postgres (docs/arquitetura.md secao 2).</summary>
    public const string Schema = "identity";

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // O parâmetro se chama "builder" (e não "modelBuilder") para casar com a assinatura da classe
    // base do ASP.NET Identity - trocar o nome dispara o analisador CA1725.
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        builder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_user");
            entity.Property(user => user.Name).HasMaxLength(120).IsRequired();
            entity.Property(user => user.CreatedAt).HasDefaultValueSql("now()");
        });

        // Renomeia as tabelas herdadas do Identity para o padrão do projeto.
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claim");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_login");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_token");

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_token");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(88).IsRequired();
            entity.Property(token => token.CreatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(token => token.TokenHash)
                  .IsUnique()
                  .HasDatabaseName("ux_refresh_token");

            // Cascata: excluir o usuário apaga os tokens dele (LGPD, US-13).
            entity.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(token => token.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Deve ser a ÚLTIMA linha: reescreve todos os nomes já configurados para snake_case.
        builder.UseSnakeCaseNames();
    }
}
