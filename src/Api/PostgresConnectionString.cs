using Npgsql;

namespace FinanceMove.Api;

/// <summary>
/// Aceita a connection string nos dois formatos que aparecem na pratica.
/// </summary>
/// <remarks>
/// O Npgsql so entende o formato chave=valor (<c>Host=...;Username=...</c>). O Railway entrega o
/// banco como URL (<c>postgresql://usuario:senha@host:5432/banco</c>) na variavel DATABASE_URL.
/// Converter aqui permite referenciar a variavel do Railway direto, sem copiar a senha do banco
/// para outro lugar (docs/deploy.md).
/// </remarks>
internal static class PostgresConnectionString
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
        };

        // Repassa o sslmode da URL quando vier (ex.: ?sslmode=require). Sem ele, vale o padrao
        // do Npgsql (Prefer): usa TLS quando o servidor oferece.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);

            if (parts.Length == 2
                && parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(parts[1].Replace("-", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
        }

        return builder.ConnectionString;
    }
}
