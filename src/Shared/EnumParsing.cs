namespace FinanceMove.Shared;

/// <summary>
/// Converte o texto snake_case que a API usa no contrato ("credit_card", "expense") de volta para
/// o membro do enum ("CreditCard", "Expense").
/// </summary>
/// <remarks>
/// Existe porque o <c>JsonStringEnumConverter</c> configurado no host vale apenas para o CORPO e a
/// RESPOSTA em JSON. Parametro de query string nao passa pelo serializador: o binding das minimal
/// APIs tenta converter o texto por conta propria e falha em "credit_card", porque o membro se
/// chama CreditCard.
/// <para>
/// Por isso os endpoints recebem os filtros de enum como <c>string?</c> e passam por aqui, em vez
/// de declarar o tipo do enum direto na assinatura.
/// </para>
/// </remarks>
public static class EnumParsing
{
    /// <summary>
    /// Devolve <c>true</c> quando o texto corresponde a um membro do enum, ignorando underscores
    /// e diferenca de maiusculas.
    /// </summary>
    public static bool TryParseSnakeCase<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // "credit_card" vira "creditcard", que casa com CreditCard ignorando maiusculas.
        var normalized = value.Trim().Replace("_", string.Empty, StringComparison.Ordinal);

        // Recusa numero: aceitar "1" deixaria a API atrelada a ordem dos membros do enum, e
        // reordenar o enum passaria a significar outra coisa sem ninguem perceber.
        if (int.TryParse(normalized, out _))
        {
            return false;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out result);
    }

    /// <summary>
    /// Igual ao <see cref="TryParseSnakeCase{TEnum}"/>, mas devolve nulo quando o texto esta
    /// ausente e lanca 422 quando esta presente e invalido.
    /// </summary>
    /// <remarks>
    /// Ausente e invalido sao coisas diferentes: filtro nao informado significa "todos", enquanto
    /// filtro escrito errado e um erro que o cliente precisa ver, e nao um resultado silenciosamente
    /// diferente do pedido.
    /// </remarks>
    public static TEnum? ParseOptional<TEnum>(string? value, string parameterName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TryParseSnakeCase<TEnum>(value, out var parsed))
        {
            return parsed;
        }

        var allowed = string.Join(", ", Enum.GetNames<TEnum>().Select(ToSnakeCase));

        throw DomainException.Unprocessable(
            $"Valor invalido para \"{parameterName}\": \"{value}\". Use um destes: {allowed}.",
            "invalid-query-value");
    }

    /// <summary>CreditCard vira credit_card. Usado para listar os valores aceitos na mensagem de erro.</summary>
    private static string ToSnakeCase(string name)
    {
        var result = new System.Text.StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            if (char.IsUpper(name[index]) && index > 0)
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(name[index]));
        }

        return result.ToString();
    }
}
