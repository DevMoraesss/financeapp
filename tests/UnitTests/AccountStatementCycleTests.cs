using System.Globalization;
using FinanceMove.Modules.Accounts;
using FinanceMove.Modules.Accounts.Contracts;

namespace FinanceMove.UnitTests;

/// <summary>
/// Ciclo de fatura do cartao (SPEC secao 5.3), a regra que quase todo app brasileiro erra.
/// </summary>
public sealed class AccountStatementCycleTests
{
    private static Account Card(short closingDay, short dueDay) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Name = "Cartao",
        Type = AccountType.CreditCard,
        InitialBalance = 0m,
        ClosingDay = closingDay,
        DueDay = dueDay,
    };

    [Fact]
    public void Vencimento_MaiorQueFechamento_FechaEVenceNoMesmoMes()
    {
        // Fecha 03, vence 10: a fatura de setembro fecha em 03/09 e vence em 10/09,
        // cobrindo compras de 04/08 a 03/09.
        var cycle = Card(closingDay: 3, dueDay: 10).GetStatementCycle(new DateOnly(2026, 9, 1));

        Assert.Equal("2026-09", cycle.Month);
        Assert.Equal(new DateOnly(2026, 9, 3), cycle.ClosingDate);
        Assert.Equal(new DateOnly(2026, 9, 10), cycle.DueDate);
        Assert.Equal(new DateOnly(2026, 8, 4), cycle.PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 3), cycle.PeriodEnd);
    }

    [Fact]
    public void Vencimento_MenorQueFechamento_FechaNoMesAnterior()
    {
        // O caso Nubank: fecha 25, vence 02. A fatura de setembro fecha em 25/08 e vence em
        // 02/09, cobrindo compras de 26/07 a 25/08.
        var cycle = Card(closingDay: 25, dueDay: 2).GetStatementCycle(new DateOnly(2026, 9, 1));

        Assert.Equal(new DateOnly(2026, 8, 25), cycle.ClosingDate);
        Assert.Equal(new DateOnly(2026, 9, 2), cycle.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 26), cycle.PeriodStart);
        Assert.Equal(new DateOnly(2026, 8, 25), cycle.PeriodEnd);
    }

    [Fact]
    public void Vencimento_MenorQueFechamento_AtravessaOAno()
    {
        var cycle = Card(closingDay: 25, dueDay: 2).GetStatementCycle(new DateOnly(2027, 1, 1));

        Assert.Equal(new DateOnly(2026, 12, 25), cycle.ClosingDate);
        Assert.Equal(new DateOnly(2027, 1, 2), cycle.DueDate);
    }

    [Fact]
    public void ContaQueNaoECartao_NaoTemCicloDeFatura()
    {
        var checking = new Account
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "Corrente",
            Type = AccountType.Checking,
            InitialBalance = 1000m,
        };

        Assert.Throws<InvalidOperationException>(() => checking.GetStatementCycle(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void CicloAmbiguo_ERejeitado()
    {
        // Fechar e vencer no mesmo dia tornaria impossivel saber a qual fatura a compra pertence.
        Assert.Throws<ArgumentException>(() => CardCycle.For(closingDay: 10, dueDay: 10, new DateOnly(2026, 9, 1)));
    }

    [Theory]
    // Cartao que fecha 03 e vence 10 (SPEC secao 5.3, exemplo A).
    [InlineData(3, 10, "2026-08-02", "2026-08")]
    [InlineData(3, 10, "2026-08-04", "2026-09")]
    [InlineData(3, 10, "2026-09-03", "2026-09")]
    [InlineData(3, 10, "2026-09-04", "2026-10")]
    // Cartao que fecha 25 e vence 02 (exemplo B, estilo Nubank).
    [InlineData(25, 2, "2026-07-26", "2026-09")]
    [InlineData(25, 2, "2026-08-25", "2026-09")]
    [InlineData(25, 2, "2026-08-26", "2026-10")]
    public void CompraCaiNaFaturaCerta(int closingDay, int dueDay, string purchase, string expectedMonth)
    {
        var month = CardCycle.StatementMonthFor(
            closingDay,
            dueDay,
            DateOnly.Parse(purchase, CultureInfo.InvariantCulture));

        Assert.Equal(expectedMonth, month);
    }
}
