using System.Globalization;
using System.Text;
using FinanceMove.Modules.Identity.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using Microsoft.AspNetCore.Mvc;

namespace FinanceMove.Api.Endpoints;

/// <summary>Perfil e direitos da LGPD: exportar e excluir (docs/api.md secao 11).</summary>
internal static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/me").RequireAuthorization();

        group.MapGet("/", async (
            ICurrentUser user,
            IIdentityQuery identity,
            CancellationToken cancellationToken) =>
        {
            var profile = await identity.FindByIdAsync(user.Id, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            ICurrentUser user,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            var changed = await auth.ChangePasswordAsync(
                user.Id,
                request.CurrentPassword,
                request.NewPassword,
                cancellationToken);

            return changed
                ? Results.NoContent()
                : throw DomainException.Unprocessable("A senha atual esta incorreta.", "invalid-password");
        });

        // SPEC US-12: portabilidade. CSV com BOM porque o Excel brasileiro so reconhece acento
        // com ele, separador ponto e virgula porque a virgula ja e o separador decimal daqui.
        group.MapGet("/export", async (
            ICurrentUser user,
            ITransactionService transactions,
            CancellationToken cancellationToken) =>
        {
            var page = await transactions.ListAsync(
                user.Id,
                new TransactionFilter(Page: 1, Size: 200),
                cancellationToken);

            var csv = new StringBuilder();
            csv.AppendLine("Data;Descricao;Tipo;Categoria;Conta;Conta destino;Status;Parcela;Valor");

            foreach (var transaction in page.Items)
            {
                var installment = transaction.Installment is { } part ? $"{part.Number}/{part.Total}" : string.Empty;

                csv.AppendLine(string.Join(';',
                    transaction.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    Escape(transaction.Description),
                    TypeLabel(transaction.Type),
                    Escape(transaction.Category?.Name ?? string.Empty),
                    Escape(transaction.Account.Name),
                    Escape(transaction.DestinationAccount?.Name ?? string.Empty),
                    transaction.Status == TransactionStatus.Confirmed ? "Confirmada" : "Pendente",
                    installment,
                    transaction.Amount.ToString("F2", CultureInfo.GetCultureInfo("pt-BR"))));
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", "financemove-transacoes.csv");
        });

        // SPEC US-13: hard delete. Um DELETE so; o banco cascateia todos os schemas.
        group.MapDelete("/", async (
            // [FromBody] explicito: minimal APIs nao inferem corpo em DELETE, e sem o atributo a
            // aplicacao nem sobe.
            [FromBody] DeleteAccountRequest request,
            ICurrentUser user,
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (request.Confirmation != "EXCLUIR")
            {
                throw DomainException.Unprocessable(
                    "Para confirmar a exclusao, digite EXCLUIR.",
                    "missing-confirmation");
            }

            var deleted = await auth.DeleteAccountAsync(user.Id, request.Password, cancellationToken);

            if (!deleted)
            {
                throw DomainException.Unprocessable("A senha informada esta incorreta.", "invalid-password");
            }

            http.Response.Cookies.Delete("refreshToken", new CookieOptions { Path = "/api/v1/auth" });
            return Results.NoContent();
        });
    }

    private static string TypeLabel(TransactionType type) => type switch
    {
        TransactionType.Income => "Receita",
        TransactionType.Expense => "Despesa",
        _ => "Transferencia",
    };

    /// <summary>Ponto e virgula dentro do texto quebraria a coluna do CSV.</summary>
    private static string Escape(string value) => value.Replace(';', ',');
}

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed record DeleteAccountRequest(string Confirmation, string Password);
