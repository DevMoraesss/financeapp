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
        }).RequireRateLimiting(RateLimiting.AuthPolicy);

        // SPEC US-12: portabilidade. CSV com BOM porque o Excel brasileiro so reconhece acento
        // com ele, separador ponto e virgula porque a virgula ja e o separador decimal daqui.
        group.MapGet("/export", async (
            ICurrentUser user,
            ITransactionService transactions,
            CancellationToken cancellationToken) =>
        {
            // Pagina ate o fim: a listagem devolve no maximo 200 por vez, e exportar so a primeira
            // pagina entregaria um "backup" incompleto sem avisar ninguem.
            var items = new List<TransactionDto>();

            for (var pageNumber = 1; ; pageNumber++)
            {
                var page = await transactions.ListAsync(
                    user.Id,
                    new TransactionFilter(Page: pageNumber, Size: 200),
                    cancellationToken);

                items.AddRange(page.Items);

                if (pageNumber >= page.Pagination.TotalPages)
                {
                    break;
                }
            }

            var csv = new StringBuilder();
            csv.AppendLine("Data;Descricao;Tipo;Categoria;Conta;Conta destino;Status;Parcela;Fatura;Valor");

            foreach (var transaction in items)
            {
                var installment = transaction.Installment is { } part ? $"{part.Number}/{part.Total}" : string.Empty;
                var statement = transaction.StatementMonth is { Length: 7 } month ? $"{month[5..]}/{month[..4]}" : string.Empty;

                csv.AppendLine(string.Join(';',
                    transaction.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    Escape(transaction.Description),
                    TypeLabel(transaction.Type),
                    Escape(transaction.Category?.Name ?? string.Empty),
                    Escape(transaction.Account.Name),
                    Escape(transaction.DestinationAccount?.Name ?? string.Empty),
                    transaction.Status == TransactionStatus.Confirmed ? "Confirmada" : "Pendente",
                    installment,
                    statement,
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

            AuthEndpoints.DeleteRefreshCookie(http);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimiting.AuthPolicy);
    }

    private static string TypeLabel(TransactionType type) => type switch
    {
        TransactionType.Income => "Receita",
        TransactionType.Expense => "Despesa",
        _ => "Transferencia",
    };

    /// <summary>
    /// Deixa um texto do usuario seguro para uma celula do CSV.
    /// </summary>
    /// <remarks>
    /// Ponto e virgula e quebra de linha quebrariam a coluna. E texto comecando com = + - @ vira
    /// FORMULA quando o arquivo abre no Excel (CSV injection): uma descricao como
    /// <c>=HYPERLINK(...)</c> executaria ao abrir o backup. O apostrofo na frente faz o Excel
    /// tratar a celula como texto.
    /// </remarks>
    internal static string Escape(string value)
    {
        var clean = value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length > 0 && "=+-@\t".Contains(clean[0], StringComparison.Ordinal) ? $"'{clean}" : clean;
    }
}

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed record DeleteAccountRequest(string Confirmation, string Password);
