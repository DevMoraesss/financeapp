namespace FinanceMove.Shared;

/// <summary>
/// Erro de regra de negocio. O host traduz para ProblemDetails com o status informado
/// (docs/api.md secao 3).
/// </summary>
/// <remarks>
/// Use 409 quando a operacao conflita com o estado atual (fatura ja paga, nome repetido) e
/// 422 quando a sintaxe esta correta mas a regra de dominio impede (parcelar fora de cartao).
/// Recurso de outro usuario nao lanca excecao: retorna nulo e vira 404 (SPEC US-14).
/// </remarks>
public sealed class DomainException(string message, int statusCode = 422, string? errorType = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    /// <summary>Slug usado no campo `type` do ProblemDetails, por exemplo "statement-already-paid".</summary>
    public string? ErrorType { get; } = errorType;

    public static DomainException Conflict(string message, string? errorType = null) =>
        new(message, 409, errorType);

    public static DomainException Unprocessable(string message, string? errorType = null) =>
        new(message, 422, errorType);
}
