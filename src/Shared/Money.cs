namespace FinanceMove.Shared;

/// <summary>
/// Toda aritmética de dinheiro do FinanceMove passa por aqui.
/// Regra do ADR-001 (Decisão 3): <c>decimal</c> sempre, arredondamento sempre explícito, nunca float.
/// </summary>
public static class Money
{
    /// <summary>Casas decimais de um valor em reais.</summary>
    public const int Scale = 2;

    private const decimal Cent = 0.01m;

    /// <summary>
    /// Arredonda para centavos usando <see cref="MidpointRounding.AwayFromZero"/>.
    /// <para>
    /// Cuidado: o padrão do C# é arredondamento bancário - <c>Math.Round(2.5)</c> devolve <b>2</b>,
    /// não 3. Para dinheiro isso surpreende o usuário, por isso a regra aqui é sempre explícita.
    /// </para>
    /// </summary>
    public static decimal Round(decimal value) =>
        Math.Round(value, Scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Divide um total em <paramref name="parts"/> parcelas garantindo que a soma seja
    /// <b>exatamente</b> o total - sem centavo sumindo nem sobrando.
    /// <para>Exemplo: R$ 100,00 em 3x -> 33,34 + 33,33 + 33,33.</para>
    /// </summary>
    /// <remarks>
    /// Invariante garantida por teste: <c>Split(total, parts).Sum() == total</c>.
    /// O resto é distribuído nas <b>primeiras</b> parcelas (é o que os bancos fazem).
    /// </remarks>
    public static decimal[] Split(decimal total, int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        // ToZero trunca (não arredonda): garante que a base nunca ultrapasse o total.
        var baseAmount = Math.Round(total / parts, Scale, MidpointRounding.ToZero);

        var result = new decimal[parts];
        Array.Fill(result, baseAmount);

        var remainder = Round(total - (baseAmount * parts));
        for (var i = 0; remainder >= Cent && i < parts; i++, remainder -= Cent)
        {
            result[i] += Cent;
        }

        return result;
    }
}
