using System.Net;
using System.Text.Json;

namespace FinanceMove.IntegrationTests;

/// <summary>
/// Garante que os healthchecks respondem no formato de docs/api.md secao 12 - é o endpoint que o
/// Railway consulta para decidir se um deploy subiu ou deve ser revertido.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HealthEndpointTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Health_RespondeSaudavelSemTocarNoBanco()
    {
        var response = await fixture.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthReady_VerificaAConexaoComOPostgres()
    {
        var response = await fixture.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

        var checks = body.RootElement.GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "postgres");
    }
}
