using FinanceMove.Shared;

namespace FinanceMove.UnitTests;

/// <summary>
/// Testes da aritmética de dinheiro - a zona de maior risco do sistema (ADR-001, Decisão 3).
/// </summary>
public sealed class MoneyTests
{
    [Fact]
    public void Round_UsaAwayFromZero_ENaoOBancarioPadraoDoCSharp()
    {
        // O padrão do C# é arredondamento BANCÁRIO (vai para o par mais próximo):
        Assert.Equal(0.12m, Math.Round(0.125m, 2));
        Assert.Equal(0.14m, Math.Round(0.135m, 2));

        // O nosso arredonda para longe do zero, que é o que o usuário espera de dinheiro:
        Assert.Equal(0.13m, Money.Round(0.125m));
        Assert.Equal(0.14m, Money.Round(0.135m));
        Assert.Equal(-0.13m, Money.Round(-0.125m));

        // Money.Round sempre trabalha em centavos: não é atalho para arredondar a inteiros.
        Assert.Equal(2.50m, Money.Round(2.5m));
    }

    [Theory]
    [InlineData(100.00, 3)] // 33,34 + 33,33 + 33,33 - o caso clássico
    [InlineData(300.00, 3)] // divisão exata
    [InlineData(3600.00, 12)] // notebook em 12x (SPEC US-06)
    [InlineData(0.05, 3)] // centavos indivisíveis
    [InlineData(1234.57, 7)]
    [InlineData(0.01, 2)] // menos centavos que parcelas
    public void Split_SomaDasParcelas_SempreIgualAoTotal(decimal total, int parts)
    {
        var installments = Money.Split(total, parts);

        // A invariante que impede R$ 0,01 sumir ou sobrar (modelo-de-dados secao 4.4).
        Assert.Equal(total, installments.Sum());
        Assert.Equal(parts, installments.Length);
        Assert.All(installments, amount => Assert.Equal(amount, Money.Round(amount)));
    }

    [Fact]
    public void Split_DistribuiORestoNasPrimeirasParcelas()
    {
        var installments = Money.Split(100.00m, 3);

        Assert.Equal([33.34m, 33.33m, 33.33m], installments);
    }

    [Fact]
    public void Split_RejeitaEntradasInvalidas()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Split(100m, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Split(-1m, 3));
    }

    [Fact]
    public void Decimal_NaoSofreDoErroDePontoFlutuante()
    {
        // A razão de nunca usarmos double para dinheiro:
        Assert.NotEqual(0.3, 0.1 + 0.2);
        Assert.Equal(0.3m, 0.1m + 0.2m);
    }
}
