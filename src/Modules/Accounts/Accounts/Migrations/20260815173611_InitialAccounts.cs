using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceMove.Modules.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounts");

            migrationBuilder.CreateTable(
                name: "account",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initial_balance = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    closing_day = table.Column<short>(type: "smallint", nullable: true),
                    due_day = table.Column<short>(type: "smallint", nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account", x => x.id);
                    table.CheckConstraint("ck_account_card_cycle", "(\"type\" = 'credit_card'\n AND closing_day BETWEEN 1 AND 28\n AND due_day BETWEEN 1 AND 28\n AND closing_day <> due_day)\nOR\n(\"type\" <> 'credit_card' AND closing_day IS NULL AND due_day IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_user",
                schema: "accounts",
                table: "account",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_account_name",
                schema: "accounts",
                table: "account",
                columns: new[] { "user_id", "name" },
                unique: true,
                filter: "archived = false");

            // -----------------------------------------------------------------------------
            // ESCRITO À MÃO - não remova ao regerar esta migration.
            //
            // O EF Core não consegue gerar esta FK sozinho: identity.app_user pertence a OUTRO
            // DbContext (módulo Identidade), e o EF só enxerga o modelo do próprio contexto.
            //
            // Por que ela existe, sendo que a regra do projeto é "sem FK entre módulos"
            // (modelo-de-dados secao 1.1): user_id é a âncora do tenant, não uma referência de
            // domínio. Com ON DELETE CASCADE, o "excluir minha conta" da LGPD (SPEC US-13) é
            // um único DELETE e o banco garante que nada sobra - bem mais confiável que
            // depender de uma cadeia de eventos que pode falhar no meio.
            //
            // Consequência: a migration do módulo Identidade precisa rodar ANTES desta.
            // -----------------------------------------------------------------------------
            migrationBuilder.Sql("""
                ALTER TABLE accounts.account
                  ADD CONSTRAINT fk_account_user
                  FOREIGN KEY (user_id) REFERENCES identity.app_user (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A FK escrita à mão cai junto com a tabela - DROP TABLE remove as constraints dela.
            migrationBuilder.DropTable(
                name: "account",
                schema: "accounts");
        }
    }
}
