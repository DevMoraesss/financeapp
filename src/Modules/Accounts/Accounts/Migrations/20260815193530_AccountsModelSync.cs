using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceMove.Modules.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AccountsModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_card_cycle",
                schema: "accounts",
                table: "account");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_card_cycle",
                schema: "accounts",
                table: "account",
                sql: "(\"type\" = 'credit_card'\n   AND closing_day BETWEEN 1 AND 28\n   AND due_day BETWEEN 1 AND 28\n   AND closing_day <> due_day)\nOR\n(\"type\" <> 'credit_card' AND closing_day IS NULL AND due_day IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_card_cycle",
                schema: "accounts",
                table: "account");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_card_cycle",
                schema: "accounts",
                table: "account",
                sql: "(\"type\" = 'credit_card'\n AND closing_day BETWEEN 1 AND 28\n AND due_day BETWEEN 1 AND 28\n AND closing_day <> due_day)\nOR\n(\"type\" <> 'credit_card' AND closing_day IS NULL AND due_day IS NULL)");
        }
    }
}
