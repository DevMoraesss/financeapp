using FinanceMove.Api;
using FinanceMove.Api.Endpoints;
using Npgsql;

namespace FinanceMove.UnitTests;

/// <summary>
/// Helpers do host que o deploy usa: a URL do banco no formato do Railway e a celula segura do
/// CSV de exportacao.
/// </summary>
public sealed class HostHelpersTests
{
    [Fact]
    public void Normalize_UrlDoRailway_ViraConnectionStringDoNpgsql()
    {
        var result = PostgresConnectionString.Normalize(
            "postgresql://postgres:s3nh%40forte%3A1@postgres.railway.internal:5432/railway");

        var parsed = new NpgsqlConnectionStringBuilder(result);

        Assert.Equal("postgres.railway.internal", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("railway", parsed.Database);
        Assert.Equal("postgres", parsed.Username);

        // Senha com @ e : chega decodificada, exatamente como foi gerada.
        Assert.Equal("s3nh@forte:1", parsed.Password);
    }

    [Fact]
    public void Normalize_UrlSemPortaComSslmode_UsaPortaPadraoERespeitaOSsl()
    {
        var parsed = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalize("postgres://u:p@db.exemplo.com/app?sslmode=require"));

        Assert.Equal(5432, parsed.Port);
        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void Normalize_FormatoChaveValor_PassaIntacto()
    {
        const string classic = "Host=localhost;Port=5435;Database=financemove;Username=financemove;Password=x";

        Assert.Equal(classic, PostgresConnectionString.Normalize(classic));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Vazio_DevolveNulo(string? value)
    {
        Assert.Null(PostgresConnectionString.Normalize(value));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "'=HYPERLINK(\"x\")")]
    [InlineData("+5511999999999", "'+5511999999999")]
    [InlineData("-10 ajuste", "'-10 ajuste")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Mercado; Assai", "Mercado, Assai")]
    [InlineData("linha1\nlinha2", "linha1 linha2")]
    [InlineData("Mercado Assai", "Mercado Assai")]
    [InlineData("", "")]
    public void Escape_TextoDoUsuario_ViraCelulaSeguraDoCsv(string input, string expected)
    {
        Assert.Equal(expected, MeEndpoints.Escape(input));
    }
}
