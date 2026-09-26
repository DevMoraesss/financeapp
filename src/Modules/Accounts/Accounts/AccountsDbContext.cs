using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Accounts;

/// <summary>
/// DbContext do módulo Contas. Dono exclusivo do schema <c>accounts</c>.
/// </summary>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public const string Schema = "accounts";

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("account", table =>
            {
                table.HasCheckConstraint(
                    "ck_account_card_cycle",
                    """
                    ("type" = 'credit_card'
                       AND closing_day BETWEEN 1 AND 28
                       AND due_day BETWEEN 1 AND 28
                       AND closing_day <> due_day)
                    OR
                    ("type" <> 'credit_card' AND closing_day IS NULL AND due_day IS NULL)
                    """);

                // Limite so existe em cartao, e nunca negativo.
                table.HasCheckConstraint(
                    "ck_account_credit_limit",
                    """credit_limit IS NULL OR ("type" = 'credit_card' AND credit_limit >= 0)""");
            });

            entity.HasKey(account => account.Id);

            entity.Property(account => account.Name).HasMaxLength(60).IsRequired();

            // Enum gravado como texto legível ('credit_card'), não como número mágico:
            // quem abrir o banco no psql entende o dado sem consultar o código.
            entity.Property(account => account.Type)
                  .HasConversion(
                      type => NamingConventions.ToSnakeCase(type.ToString()),
                      value => Enum.Parse<AccountType>(value.Replace("_", string.Empty), ignoreCase: true))
                  .HasMaxLength(20)
                  .IsRequired();

            // A regra de ouro do projeto: dinheiro é numeric(14,2), nunca float (ADR-001, Decisão 3).
            entity.Property(account => account.InitialBalance)
                  .HasColumnType("numeric(14,2)")
                  .IsRequired();

            entity.Property(account => account.CreditLimit).HasColumnType("numeric(14,2)");

            entity.Property(account => account.Archived).HasDefaultValue(false);
            entity.Property(account => account.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(account => account.UpdatedAt).HasDefaultValueSql("now()");

            // Índice único parcial: dois nomes iguais só convivem se um deles estiver arquivado.
            entity.HasIndex(account => new { account.UserId, account.Name })
                  .IsUnique()
                  .HasFilter("archived = false")
                  .HasDatabaseName("ux_account_name");

            entity.HasIndex(account => account.UserId).HasDatabaseName("ix_account_user");
        });

        modelBuilder.UseSnakeCaseNames();
    }
}
