using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FinanceMove.IntegrationTests;

/// <summary>
/// Isolamento entre usuarios (SPEC US-14), exportacao completa (US-12) e as defesas das respostas.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IsolationAndExportTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Transacao_DeOutroUsuario_Responde404EmTodosOsVerbos()
    {
        var (alice, _) = await fixture.CreateUserAsync();
        var (bob, _) = await fixture.CreateUserAsync();

        var account = await CreateAccountAsync(alice, "Corrente", "checking");
        var transaction = await CreateExpenseAsync(alice, account, "Mercado", 150.00m, new DateOnly(2026, 8, 14));

        // 404, e nunca 403: 403 confirmaria para o Bob que o id existe.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/transactions/{transaction}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/v1/transactions/{transaction}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/accounts/{account}")).StatusCode);

        // E o lancamento da Alice continua la, intacto.
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/v1/transactions/{transaction}")).StatusCode);
    }

    [Fact]
    public async Task Export_MaisDe200Lancamentos_TrazTodosSemRepetir()
    {
        var (client, _) = await fixture.CreateUserAsync();
        var checking = await CreateAccountAsync(client, "Corrente", "checking");
        var card = await CreateAccountAsync(client, "Roxinho", "credit_card", closingDay: 3, dueDay: 10);
        var category = await CategoryIdAsync(client, "Lazer");

        // 48 parcelas nascem com o MESMO CreatedAt: e o caso que embaralhava a paginacao.
        var installments = await client.PostAsJsonAsync("/api/v1/transactions/installments", new
        {
            description = "Notebook",
            totalAmount = 4800.00m,
            installments = 48,
            date = "2026-01-15",
            cardId = card,
            categoryId = category,
        });
        installments.EnsureSuccessStatusCode();

        for (var index = 1; index <= 160; index++)
        {
            await CreateExpenseAsync(client, checking, $"Gasto {index}", 10.00m, new DateOnly(2026, 8, 1 + (index % 28)));
        }

        var csv = await ExportAsync(client);
        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        var descriptions = rows.Select(row => row.Split(';')[1]).ToList();

        Assert.Equal(208, rows.Count);
        Assert.Equal(208, descriptions.Distinct().Count());
        Assert.Contains("Notebook 48/48", descriptions);
    }

    [Fact]
    public async Task Export_DescricaoQueParecerFormula_SaiComoTexto()
    {
        var (client, _) = await fixture.CreateUserAsync();
        var account = await CreateAccountAsync(client, "Corrente", "checking");
        await CreateExpenseAsync(client, account, "=HYPERLINK(\"http://x\")", 1.00m, new DateOnly(2026, 8, 1));

        var csv = await ExportAsync(client);

        Assert.Contains(";'=HYPERLINK(\"http://x\");", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Categoria_CorQueNaoEHexadecimal_Responde422()
    {
        var (client, _) = await fixture.CreateUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/categories", new
        {
            name = "Pets",
            type = "expense",
            color = "red;background:url(x)",
            icon = "paw-print",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Api_Resposta_TemCabecalhosDeSegurancaESemCache()
    {
        var (client, _) = await fixture.CreateUserAsync();

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    // -----------------------------------------------------------------------------------------

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string name,
        string type,
        int? closingDay = null,
        int? dueDay = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/accounts", new
        {
            name,
            type,
            initialBalance = 1000.00m,
            closingDay,
            dueDay,
        });

        response.EnsureSuccessStatusCode();
        return await IdOfAsync(response);
    }

    private static async Task<Guid> CreateExpenseAsync(HttpClient client, Guid account, string description, decimal amount, DateOnly date)
    {
        var category = await CategoryIdAsync(client, "Mercado");

        var response = await client.PostAsJsonAsync("/api/v1/transactions", new
        {
            type = "expense",
            amount,
            date = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            description,
            accountId = account,
            categoryId = category,
        });

        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("transaction").GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string name)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/categories?type=expense"));

        return body.RootElement.GetProperty("categories").EnumerateArray()
            .First(category => category.GetProperty("name").GetString() == name)
            .GetProperty("id").GetGuid();
    }

    private static async Task<string> ExportAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me/export");
        response.EnsureSuccessStatusCode();
        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).TrimStart((char)0xFEFF); // BOM que o Excel exige
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
