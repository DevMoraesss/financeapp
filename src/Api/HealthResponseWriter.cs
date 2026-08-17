using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinanceMove.Api;

/// <summary>
/// Escreve a resposta dos healthchecks no formato documentado em docs/api.md secao 12.
/// O padrão do ASP.NET devolve só a string "Healthy"; aqui devolvemos JSON com duração e detalhes,
/// que é o que serve para depurar deploy.
/// </summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            version = typeof(HealthResponseWriter).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Count == 0
                ? null
                : report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMs = (int)entry.Value.Duration.TotalMilliseconds,
                    // A mensagem de erro fica só no log do servidor: detalhe técnico de banco
                    // nunca vai no corpo da resposta (docs/api.md secao 3).
                    error = entry.Value.Exception is null ? null : "ver logs do servidor",
                }).ToArray(),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options));
    }
}
