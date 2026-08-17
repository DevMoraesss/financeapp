using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;

namespace FinanceMove.UnitTests;

/// <summary>
/// Conversao dos filtros de enum que chegam pela query string.
/// </summary>
/// <remarks>
/// Estes testes existem por causa de um bug real: os endpoints declaravam o tipo do enum direto na
/// assinatura, e o binding das minimal APIs falhava em "expense" porque query string nao passa
/// pelo JsonStringEnumConverter configurado no host. O resultado era 500 em
/// GET /api/v1/categories?type=expense.
/// </remarks>
public sealed class EnumParsingTests
{
    [Theory]
    [InlineData("expense", CategoryKind.Expense)]
    [InlineData("income", CategoryKind.Income)]
    [InlineData("Expense", CategoryKind.Expense)]
    [InlineData("EXPENSE", CategoryKind.Expense)]
    [InlineData(" expense ", CategoryKind.Expense)]
    public void ParseOptional_AceitaOTextoDoContrato(string entrada, CategoryKind esperado)
    {
        Assert.Equal(esperado, EnumParsing.ParseOptional<CategoryKind>(entrada, "type"));
    }

    [Theory]
    [InlineData("credit_card", AccountType.CreditCard)]
    [InlineData("creditcard", AccountType.CreditCard)]
    [InlineData("checking", AccountType.Checking)]
    [InlineData("savings", AccountType.Savings)]
    [InlineData("cash", AccountType.Cash)]
    public void ParseOptional_EntendeSnakeCaseDeNomeComposto(string entrada, AccountType esperado)
    {
        Assert.Equal(esperado, EnumParsing.ParseOptional<AccountType>(entrada, "type"));
    }

    [Theory]
    [InlineData("transfer", TransactionType.Transfer)]
    [InlineData("pending", TransactionStatus.Pending)]
    public void ParseOptional_FuncionaParaTodosOsEnumsDoContrato(string entrada, object esperado)
    {
        var resultado = esperado is TransactionType
            ? EnumParsing.ParseOptional<TransactionType>(entrada, "type")
            : (object?)EnumParsing.ParseOptional<TransactionStatus>(entrada, "status");

        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseOptional_SemValorSignificaSemFiltro(string? entrada)
    {
        // Filtro ausente quer dizer "todos", e nao erro.
        Assert.Null(EnumParsing.ParseOptional<CategoryKind>(entrada, "type"));
    }

    [Theory]
    [InlineData("despesa")]
    [InlineData("expenses")]
    [InlineData("xpto")]
    public void ParseOptional_ValorErradoLancaComAListaDosAceitos(string entrada)
    {
        var erro = Assert.Throws<DomainException>(() => EnumParsing.ParseOptional<CategoryKind>(entrada, "type"));

        Assert.Equal(422, erro.StatusCode);
        Assert.Contains(entrada, erro.Message, StringComparison.Ordinal);

        // A mensagem precisa dizer o que vale, senao quem chamou fica adivinhando.
        Assert.Contains("expense", erro.Message, StringComparison.Ordinal);
        Assert.Contains("income", erro.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void ParseOptional_RecusaNumero(string entrada)
    {
        // Aceitar numero atrelaria a API a ordem dos membros do enum: reordenar o enum passaria a
        // significar outra coisa sem ninguem perceber.
        Assert.Throws<DomainException>(() => EnumParsing.ParseOptional<CategoryKind>(entrada, "type"));
    }

    [Fact]
    public void TryParseSnakeCase_NaoLancaEmValorInvalido()
    {
        Assert.False(EnumParsing.TryParseSnakeCase<CategoryKind>("nada", out var resultado));
        Assert.Equal(default, resultado);
    }
}
