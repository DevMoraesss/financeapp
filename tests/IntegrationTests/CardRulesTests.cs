using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceMove.Modules.Accounts.Contracts;

namespace FinanceMove.IntegrationTests;

/// <summary>
/// Regras do cartao revistas com o usuario em 25/09/2026 (SPEC D12): despesa de cartao conta no mes
/// da fatura, a divida do cartao inclui as parcelas futuras, o limite nunca vira saldo e o dashboard
/// separa contas de cartoes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardRulesTests(PostgresFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")));

    [Fact]
    public async Task Parcelamento_CompraDeJaneiro_ContaNosMesesDasFaturas()
    {
        var (client, _) = await fixture.CreateUserAsync();
        var checking = await CreateAccountAsync(client, "Corrente", "checking", 1000m);
        var card = await CreateAccountAsync(client, "Roxinho", "credit_card", 0m, closingDay: 3, dueDay: 10);
        var lazer = await CategoryIdAsync(client, "Lazer");

        // Fecha dia 03, vence dia 10: compra de 14/01 cai na fatura de 02/2026, e as parcelas
        // seguintes em 03 e 04. Em janeiro, nada saiu do bolso por causa dela.
        (await client.PostAsJsonAsync("/api/v1/transactions/installments", new
        {
            description = "Tenis",
            totalAmount = 300.00m,
            installments = 3,
            date = "2026-01-14",
            cardId = card,
            categoryId = lazer,
        })).EnsureSuccessStatusCode();

        await CreateExpenseAsync(client, checking, "Padaria", 50.00m, new DateOnly(2026, 1, 20));

        Assert.Equal(50.00m, await MonthExpensesAsync(client, "2026-01"));
        Assert.Equal(100.00m, await MonthExpensesAsync(client, "2026-02"));
        Assert.Equal(100.00m, await MonthExpensesAsync(client, "2026-03"));
        Assert.Equal(100.00m, await MonthExpensesAsync(client, "2026-04"));

        // A lista do mes e o orcamento seguem a mesma regra.
        var february = await ListDescriptionsAsync(client, "2026-02");
        Assert.Contains("Tenis 1/3", february);
        Assert.DoesNotContain("Tenis 1/3", await ListDescriptionsAsync(client, "2026-01"));

        (await client.PutAsJsonAsync($"/api/v1/budgets/{lazer}", new { monthlyLimit = 500.00m })).EnsureSuccessStatusCode();
        Assert.Equal(0.00m, await BudgetSpentAsync(client, "2026-01", lazer));
        Assert.Equal(100.00m, await BudgetSpentAsync(client, "2026-02", lazer));
    }

    [Fact]
    public async Task SaldoDoCartao_ParcelasFuturas_EntramNaDividaENoLimite()
    {
        var (client, _) = await fixture.CreateUserAsync();
        var checking = await CreateAccountAsync(client, "Corrente", "checking", 1000m);
        var card = await CreateAccountAsync(client, "Itau", "credit_card", 0m, closingDay: 3, dueDay: 10, creditLimit: 5000m);
        var lazer = await CategoryIdAsync(client, "Lazer");

        (await client.PostAsJsonAsync("/api/v1/transactions/installments", new
        {
            description = "Perfume",
            totalAmount = 300.00m,
            installments = 3,
            date = Iso(Today.AddDays(-10)),
            cardId = card,
            categoryId = lazer,
        })).EnsureSuccessStatusCode();

        // Lancamento futuro na conta corrente ainda nao aconteceu: nao mexe no saldo dela.
        await CreateExpenseAsync(client, checking, "Aluguel agendado", 800.00m, Today.AddDays(30));

        using var accounts = JsonDocument.Parse(await client.GetStringAsync("/api/v1/accounts"));
        var cardJson = accounts.RootElement.GetProperty("accounts").EnumerateArray()
            .Single(account => account.GetProperty("id").GetGuid() == card);

        // As 3 parcelas ja ocupam o limite, mesmo as de meses que ainda nao chegaram.
        Assert.Equal(-300.00m, cardJson.GetProperty("currentBalance").GetDecimal());
        Assert.Equal(5000.00m, cardJson.GetProperty("creditLimit").GetDecimal());
        Assert.Equal(4700.00m, cardJson.GetProperty("availableLimit").GetDecimal());

        using var dashboard = JsonDocument.Parse(await client.GetStringAsync("/api/v1/dashboard"));
        var root = dashboard.RootElement;

        Assert.Equal(1000.00m, root.GetProperty("accountsBalance").GetDecimal());
        Assert.Equal(300.00m, root.GetProperty("cardsOwed").GetDecimal());

        var cardOverview = root.GetProperty("cards").EnumerateArray().Single();
        Assert.Equal(300.00m, cardOverview.GetProperty("owed").GetDecimal());
        Assert.Equal(4700.00m, cardOverview.GetProperty("availableLimit").GetDecimal());
        Assert.Equal(JsonValueKind.Object, cardOverview.GetProperty("currentStatement").ValueKind);
    }

    [Fact]
    public async Task CriarCartao_SaldoInicialPositivo_Responde422()
    {
        var (client, _) = await fixture.CreateUserAsync();

        // O engano que aconteceu em producao: o limite digitado como saldo inicial.
        var response = await client.PostAsJsonAsync("/api/v1/accounts", new
        {
            name = "Itau",
            type = "credit_card",
            initialBalance = 5000.00m,
            closingDay = 3,
            dueDay = 10,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("https://financemove.app/errors/card-positive-initial-balance", await ProblemTypeAsync(response));
    }

    [Fact]
    public async Task CriarConta_LimiteEmContaQueNaoECartao_Responde422()
    {
        var (client, _) = await fixture.CreateUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/accounts", new
        {
            name = "Corrente",
            type = "checking",
            initialBalance = 100.00m,
            creditLimit = 1000.00m,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("https://financemove.app/errors/credit-limit-only-card", await ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Dashboard_UltimasTransacoes_NaoMostraLancamentoFuturo()
    {
        var (client, _) = await fixture.CreateUserAsync();
        var checking = await CreateAccountAsync(client, "Corrente", "checking", 1000m);

        await CreateExpenseAsync(client, checking, "Ja aconteceu", 10.00m, Today.AddDays(-1));
        await CreateExpenseAsync(client, checking, "Ainda vai acontecer", 10.00m, Today.AddDays(40));

        using var dashboard = JsonDocument.Parse(await client.GetStringAsync("/api/v1/dashboard"));
        var recent = dashboard.RootElement.GetProperty("recentTransactions").EnumerateArray()
            .Select(item => item.GetProperty("description").GetString())
            .ToList();

        Assert.Contains("Ja aconteceu", recent);
        Assert.DoesNotContain("Ainda vai acontecer", recent);
    }

    [Fact]
    public async Task Recorrencia_NoCartao_NasceNaFaturaCerta()
    {
        var (client, _) = await fixture.CreateUserAsync();
        var card = await CreateAccountAsync(client, "Itau", "credit_card", 0m, closingDay: 3, dueDay: 10);
        var assinaturas = await CategoryIdAsync(client, "Assinaturas");
        var startsOn = Today.AddDays(-35);

        (await client.PostAsJsonAsync("/api/v1/recurrences", new
        {
            description = "Streaming",
            amount = 39.90m,
            type = "expense",
            frequency = "monthly",
            referenceDay = Math.Min(startsOn.Day, 28),
            startsOn = Iso(startsOn),
            accountId = card,
            categoryId = assinaturas,
        })).EnsureSuccessStatusCode();

        // O catch-up roda na primeira requisicao do dia de cada usuario, e esse usuario ja fez a
        // dele. Uma API nova (cache em memoria vazio) com o mesmo token forca a rodada.
        await using var freshApi = fixture.CreateFactory();
        var fresh = freshApi.CreateClient();
        fresh.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

        using var pending = JsonDocument.Parse(await fresh.GetStringAsync("/api/v1/transactions?status=pending"));
        var items = pending.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            var date = DateOnly.Parse(item.GetProperty("date").GetString()!, CultureInfo.InvariantCulture);
            Assert.Equal(CardCycle.StatementMonthFor(3, 10, date), item.GetProperty("statementMonth").GetString());
        }
    }

    // -----------------------------------------------------------------------------------------

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string name,
        string type,
        decimal initialBalance,
        int? closingDay = null,
        int? dueDay = null,
        decimal? creditLimit = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/accounts", new
        {
            name,
            type,
            initialBalance,
            closingDay,
            dueDay,
            creditLimit,
        });

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task CreateExpenseAsync(HttpClient client, Guid account, string description, decimal amount, DateOnly date)
    {
        var category = await CategoryIdAsync(client, "Mercado");

        (await client.PostAsJsonAsync("/api/v1/transactions", new
        {
            type = "expense",
            amount,
            date = Iso(date),
            description,
            accountId = account,
            categoryId = category,
        })).EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string name)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/categories?type=expense"));

        return body.RootElement.GetProperty("categories").EnumerateArray()
            .First(category => category.GetProperty("name").GetString() == name)
            .GetProperty("id").GetGuid();
    }

    private static async Task<decimal> MonthExpensesAsync(HttpClient client, string month)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/dashboard?month={month}"));
        return body.RootElement.GetProperty("month").GetProperty("expenses").GetDecimal();
    }

    private static async Task<List<string?>> ListDescriptionsAsync(HttpClient client, string month)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/transactions?month={month}"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("description").GetString())
            .ToList();
    }

    private static async Task<decimal> BudgetSpentAsync(HttpClient client, string month, Guid category)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/budgets?month={month}"));

        return body.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("categoryId").GetGuid() == category)
            .GetProperty("spent").GetDecimal();
    }

    private static async Task<string?> ProblemTypeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("type").GetString();
    }
}
