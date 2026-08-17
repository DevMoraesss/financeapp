using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceMove.Modules.Transactions.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transactions");

            migrationBuilder.CreateTable(
                name: "category",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "installment_group",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    installments = table.Column<short>(type: "smallint", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installment_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recurrence_rule",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    frequency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reference_day = table.Column<short>(type: "smallint", nullable: false),
                    reference_month = table.Column<short>(type: "smallint", nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    next_run_on = table.Column<DateOnly>(type: "date", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurrence_rule", x => x.id);
                    table.CheckConstraint("ck_recurrence_day", "(frequency = 'Weekly' AND reference_day BETWEEN 1 AND 7 AND reference_month IS NULL)\nOR (frequency = 'Monthly' AND reference_day BETWEEN 1 AND 28 AND reference_month IS NULL)\nOR (frequency = 'Yearly' AND reference_day BETWEEN 1 AND 28 AND reference_month BETWEEN 1 AND 12)");
                    table.ForeignKey(
                        name: "fk_recurrence_rule_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "transactions",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transaction",
                schema: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recurrence_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installment_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installment_number = table.Column<short>(type: "smallint", nullable: true),
                    installment_total = table.Column<short>(type: "smallint", nullable: true),
                    statement_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    external_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transaction", x => x.id);
                    table.CheckConstraint("ck_transaction_amount", "amount > 0");
                    table.CheckConstraint("ck_transaction_installment", "(installment_group_id IS NULL AND installment_number IS NULL AND installment_total IS NULL)\nOR\n(installment_group_id IS NOT NULL AND installment_number BETWEEN 1 AND installment_total)");
                    table.CheckConstraint("ck_transaction_shape", "(\"type\" = 'Transfer'\n   AND destination_account_id IS NOT NULL\n   AND destination_account_id <> account_id\n   AND category_id IS NULL)\nOR\n(\"type\" IN ('Income','Expense')\n   AND destination_account_id IS NULL\n   AND category_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_transaction_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "transactions",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transaction_installment_group_installment_group_id",
                        column: x => x.installment_group_id,
                        principalSchema: "transactions",
                        principalTable: "installment_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transaction_recurrence_rule_recurrence_rule_id",
                        column: x => x.recurrence_rule_id,
                        principalSchema: "transactions",
                        principalTable: "recurrence_rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ux_category_name",
                schema: "transactions",
                table: "category",
                columns: new[] { "user_id", "name", "type" },
                unique: true,
                filter: "archived = false");

            migrationBuilder.CreateIndex(
                name: "ix_installment_group_user",
                schema: "transactions",
                table: "installment_group",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurrence_next",
                schema: "transactions",
                table: "recurrence_rule",
                column: "next_run_on",
                filter: "active");

            migrationBuilder.CreateIndex(
                name: "ix_recurrence_rule_category_id",
                schema: "transactions",
                table: "recurrence_rule",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_account",
                schema: "transactions",
                table: "transaction",
                columns: new[] { "user_id", "account_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_transaction_category",
                schema: "transactions",
                table: "transaction",
                columns: new[] { "user_id", "category_id", "date" },
                filter: "status = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_category_id",
                schema: "transactions",
                table: "transaction",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_dest",
                schema: "transactions",
                table: "transaction",
                columns: new[] { "user_id", "destination_account_id", "date" },
                filter: "destination_account_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_installment_group_id",
                schema: "transactions",
                table: "transaction",
                column: "installment_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_list",
                schema: "transactions",
                table: "transaction",
                columns: new[] { "user_id", "date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_transaction_recurrence_rule_id",
                schema: "transactions",
                table: "transaction",
                column: "recurrence_rule_id");

            migrationBuilder.CreateIndex(
                name: "ux_transaction_external",
                schema: "transactions",
                table: "transaction",
                columns: new[] { "user_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            // -----------------------------------------------------------------------------
            // ESCRITO A MAO. Nao remova ao regerar esta migration.
            //
            // O EF Core nao gera estas chaves estrangeiras sozinho: identity.app_user pertence a
            // OUTRO DbContext, e o EF so enxerga o modelo do proprio contexto.
            //
            // Elas existem porque user_id e a ancora do tenant, nao uma referencia de dominio.
            // Com ON DELETE CASCADE, o "excluir minha conta" da LGPD (SPEC US-13) e um unico
            // DELETE e o banco garante que nada sobra (modelo-de-dados secao 1.1).
            //
            // Consequencia: a migration do modulo Identidade precisa rodar ANTES desta.
            // -----------------------------------------------------------------------------
            migrationBuilder.Sql("""
                ALTER TABLE transactions.category
                  ADD CONSTRAINT fk_category_user
                  FOREIGN KEY (user_id) REFERENCES identity.app_user (id) ON DELETE CASCADE;

                ALTER TABLE transactions.installment_group
                  ADD CONSTRAINT fk_installment_group_user
                  FOREIGN KEY (user_id) REFERENCES identity.app_user (id) ON DELETE CASCADE;

                ALTER TABLE transactions.recurrence_rule
                  ADD CONSTRAINT fk_recurrence_rule_user
                  FOREIGN KEY (user_id) REFERENCES identity.app_user (id) ON DELETE CASCADE;

                ALTER TABLE transactions.transaction
                  ADD CONSTRAINT fk_transaction_user
                  FOREIGN KEY (user_id) REFERENCES identity.app_user (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "installment_group",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "recurrence_rule",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "category",
                schema: "transactions");
        }
    }
}
