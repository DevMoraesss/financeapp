using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceMove.Modules.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AccountsCreditLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "credit_limit",
                schema: "accounts",
                table: "account",
                type: "numeric(14,2)",
                nullable: true);

            // ESCRITO A MAO. Correcao de dado real: ate esta versao o formulario pedia "saldo
            // inicial: quanto voce tem nesta conta hoje" tambem para cartao, e o limite do cartao
            // foi digitado ali. Como saldo, o limite virava dinheiro e inflava o saldo total.
            // Saldo inicial POSITIVO em cartao so acontece por esse engano (saldo de cartao e
            // divida: zero ou negativo), entao ele vira o limite e o saldo inicial volta a zero.
            migrationBuilder.Sql("""
                UPDATE accounts.account
                   SET credit_limit = initial_balance,
                       initial_balance = 0,
                       updated_at = now()
                 WHERE "type" = 'credit_card'
                   AND initial_balance > 0;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_credit_limit",
                schema: "accounts",
                table: "account",
                sql: "credit_limit IS NULL OR (\"type\" = 'credit_card' AND credit_limit >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volta ao estado anterior, inclusive o engano: o limite retorna ao saldo inicial.
            migrationBuilder.Sql("""
                UPDATE accounts.account
                   SET initial_balance = credit_limit
                 WHERE "type" = 'credit_card'
                   AND initial_balance = 0
                   AND credit_limit IS NOT NULL;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_credit_limit",
                schema: "accounts",
                table: "account");

            migrationBuilder.DropColumn(
                name: "credit_limit",
                schema: "accounts",
                table: "account");
        }
    }
}
