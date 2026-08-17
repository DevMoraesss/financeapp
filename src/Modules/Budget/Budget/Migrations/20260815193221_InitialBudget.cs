using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceMove.Modules.Budget.Migrations
{
    /// <inheritdoc />
    public partial class InitialBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "budget");

            migrationBuilder.CreateTable(
                name: "budget",
                schema: "budget",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_limit = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget", x => x.id);
                    table.CheckConstraint("ck_budget_limit", "monthly_limit > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_budget_category",
                schema: "budget",
                table: "budget",
                columns: new[] { "user_id", "category_id" },
                unique: true);

            // ESCRITO A MAO. Ver a explicacao completa na migration InitialTransactions:
            // user_id e a ancora do tenant e a unica referencia entre modulos com chave
            // estrangeira. Note que category_id NAO tem FK: categoria pertence ao modulo
            // Transactions e a referencia entre modulos de negocio e apenas logica,
            // validada por contrato (modelo-de-dados secao 1.1).
            migrationBuilder.Sql("""
                ALTER TABLE budget.budget
                  ADD CONSTRAINT fk_budget_user
                  FOREIGN KEY (user_id) REFERENCES identity.app_user (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget",
                schema: "budget");
        }
    }
}
