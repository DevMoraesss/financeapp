using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Transactions;

/// <summary>DbContext do modulo Transactions. Dono exclusivo do schema transactions.</summary>
public sealed class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options) : DbContext(options)
{
    public const string Schema = "transactions";

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<InstallmentGroup> InstallmentGroups => Set<InstallmentGroup>();

    public DbSet<RecurrenceRule> RecurrenceRules => Set<RecurrenceRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("category");
            entity.HasKey(category => category.Id);

            entity.Property(category => category.Name).HasMaxLength(40).IsRequired();
            entity.Property(category => category.Color).HasMaxLength(7).IsRequired();
            entity.Property(category => category.Icon).HasMaxLength(40).IsRequired();
            entity.Property(category => category.Type).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(category => category.System).HasDefaultValue(false);
            entity.Property(category => category.Archived).HasDefaultValue(false);
            entity.Property(category => category.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(category => category.UpdatedAt).HasDefaultValueSql("now()");

            // Duas categorias com o mesmo nome so convivem se tiverem tipos diferentes. E o que
            // permite as duas linhas "Ajuste" (uma receita e uma despesa) do seed.
            entity.HasIndex(category => new { category.UserId, category.Name, category.Type })
                  .IsUnique()
                  .HasFilter("archived = false")
                  .HasDatabaseName("ux_category_name");
        });

        modelBuilder.Entity<InstallmentGroup>(entity =>
        {
            entity.ToTable("installment_group");
            entity.HasKey(group => group.Id);
            entity.Property(group => group.Description).HasMaxLength(120).IsRequired();
            entity.Property(group => group.TotalAmount).HasColumnType("numeric(14,2)").IsRequired();
            entity.Property(group => group.CreatedAt).HasDefaultValueSql("now()");
            entity.HasIndex(group => group.UserId).HasDatabaseName("ix_installment_group_user");
        });

        modelBuilder.Entity<RecurrenceRule>(entity =>
        {
            entity.ToTable("recurrence_rule", table => table.HasCheckConstraint(
                "ck_recurrence_day",
                """
                (frequency = 'Weekly' AND reference_day BETWEEN 1 AND 7 AND reference_month IS NULL)
                OR (frequency = 'Monthly' AND reference_day BETWEEN 1 AND 28 AND reference_month IS NULL)
                OR (frequency = 'Yearly' AND reference_day BETWEEN 1 AND 28 AND reference_month BETWEEN 1 AND 12)
                """));

            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Description).HasMaxLength(120).IsRequired();
            entity.Property(rule => rule.Amount).HasColumnType("numeric(14,2)").IsRequired();
            entity.Property(rule => rule.Type).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(rule => rule.Frequency).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(rule => rule.Active).HasDefaultValue(true);
            entity.Property(rule => rule.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(rule => rule.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasOne(rule => rule.Category)
                  .WithMany()
                  .HasForeignKey(rule => rule.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Indice parcial: o job de catch-up acha as regras vencidas sem varrer a tabela toda.
            entity.HasIndex(rule => rule.NextRunOn)
                  .HasFilter("active")
                  .HasDatabaseName("ix_recurrence_next");
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.ToTable("transaction", table =>
            {
                // A ultima tranca: mesmo um bug futuro na aplicacao nao consegue gravar
                // "transferencia com categoria" nem "despesa sem categoria".
                table.HasCheckConstraint(
                    "ck_transaction_shape",
                    """
                    ("type" = 'Transfer'
                       AND destination_account_id IS NOT NULL
                       AND destination_account_id <> account_id
                       AND category_id IS NULL)
                    OR
                    ("type" IN ('Income','Expense')
                       AND destination_account_id IS NULL
                       AND category_id IS NOT NULL)
                    """);

                table.HasCheckConstraint("ck_transaction_amount", "amount > 0");

                table.HasCheckConstraint(
                    "ck_transaction_installment",
                    """
                    (installment_group_id IS NULL AND installment_number IS NULL AND installment_total IS NULL)
                    OR
                    (installment_group_id IS NOT NULL AND installment_number BETWEEN 1 AND installment_total)
                    """);
            });

            entity.HasKey(transaction => transaction.Id);

            entity.Property(transaction => transaction.Type).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(transaction => transaction.Status).HasConversion<string>().HasMaxLength(10).IsRequired();
            entity.Property(transaction => transaction.Description).HasMaxLength(120).IsRequired();
            entity.Property(transaction => transaction.StatementMonth).HasMaxLength(7);
            entity.Property(transaction => transaction.ExternalId).HasMaxLength(120);
            entity.Property(transaction => transaction.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(transaction => transaction.UpdatedAt).HasDefaultValueSql("now()");

            // A regra numero um do projeto: dinheiro e numeric(14,2), nunca float.
            entity.Property(transaction => transaction.Amount).HasColumnType("numeric(14,2)").IsRequired();

            entity.HasOne(transaction => transaction.Category)
                  .WithMany()
                  .HasForeignKey(transaction => transaction.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<InstallmentGroup>()
                  .WithMany()
                  .HasForeignKey(transaction => transaction.InstallmentGroupId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<RecurrenceRule>()
                  .WithMany()
                  .HasForeignKey(transaction => transaction.RecurrenceRuleId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(transaction => new { transaction.UserId, transaction.Date })
                  .IsDescending(false, true)
                  .HasDatabaseName("ix_transaction_list");

            entity.HasIndex(transaction => new { transaction.UserId, transaction.AccountId, transaction.Date })
                  .HasDatabaseName("ix_transaction_account");

            entity.HasIndex(transaction => new { transaction.UserId, transaction.DestinationAccountId, transaction.Date })
                  .HasFilter("destination_account_id IS NOT NULL")
                  .HasDatabaseName("ix_transaction_dest");

            entity.HasIndex(transaction => new { transaction.UserId, transaction.CategoryId, transaction.Date })
                  .HasFilter("status = 'Confirmed'")
                  .HasDatabaseName("ix_transaction_category");

            entity.HasIndex(transaction => new { transaction.UserId, transaction.ExternalId })
                  .IsUnique()
                  .HasFilter("external_id IS NOT NULL")
                  .HasDatabaseName("ux_transaction_external");
        });

        modelBuilder.UseSnakeCaseNames();
    }
}
