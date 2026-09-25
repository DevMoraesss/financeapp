using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace FinanceMove.IntegrationTests;

/// <summary>
/// As trancas de autenticacao que o deploy publico exige (docs/ADR-002): cadastro por convite,
/// teto de usuarios, senha minima, rotacao de sessao sem derrubar abas e rate limit.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthHardeningTests(PostgresFixture fixture)
{
    private const string Password = "senhaDeTeste123";

    [Fact]
    public async Task Register_ConviteConfiguradoESemCodigo_Responde403()
    {
        await using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["Registration:InviteCode"] = "convite-secreto",
        });

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", NewUser());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("https://financemove.app/errors/invalid-invite", await ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Register_ConviteConfiguradoECodigoCerto_CriaConta()
    {
        await using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["Registration:InviteCode"] = "convite-secreto",
        });

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            NewUser(inviteCode: " convite-secreto "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Register_SemConviteESemCadastroAberto_Responde403()
    {
        // O padrao de producao: nada configurado significa cadastro FECHADO, nunca aberto.
        await using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["Registration:Open"] = "false",
            ["Registration:InviteCode"] = string.Empty,
        });

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", NewUser());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("https://financemove.app/errors/registration-closed", await ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Register_LimiteDeUsuariosAtingido_Responde403()
    {
        int existing;

        await using (var identity = fixture.CreateIdentityContext())
        {
            existing = identity.Users.Count();
        }

        await using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["Registration:MaxUsers"] = existing.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", NewUser());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("https://financemove.app/errors/user-limit-reached", await ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Register_SenhaComMenosDe12Caracteres_Responde422()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/register", NewUser(password: "curta12345"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Login_CookieDeSessao_EHttpOnlyStrictERestritoAoAuth()
    {
        var (_, email) = await fixture.CreateUserAsync(Password);
        var client = CookielessClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var setCookie = login.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("refreshToken=", StringComparison.Ordinal));

        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_MesmoCookieDuasVezesDentroDaJanela_NaoDerrubaASessao()
    {
        // Regressao do bug que deslogava o usuario a cada 15 minutos: tres requisicoes da mesma
        // tela (ou duas abas) renovam com o MESMO cookie quase ao mesmo tempo.
        var client = CookielessClient();
        var original = await LoginCookieAsync(client);

        var first = await RefreshAsync(client, original);
        var second = await RefreshAsync(client, original);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // E as duas sessoes novas continuam vivas.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, RefreshCookieOf(first))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, RefreshCookieOf(second))).StatusCode);
    }

    [Fact]
    public async Task Refresh_ChamadasSimultaneasComOMesmoCookie_TodasRecebemSessao()
    {
        var client = CookielessClient();
        var original = await LoginCookieAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(client, original)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }

    [Fact]
    public async Task Refresh_TokenReusadoForaDaJanela_DerrubaTodasAsSessoes()
    {
        var client = CookielessClient();
        var (original, email) = await LoginCookieWithEmailAsync(client);

        var rotated = await RefreshAsync(client, original);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        // Empurra a rotacao para 1 minuto atras: o reuso agora cai fora da janela de 30 s.
        await ExecuteSqlAsync(
            """
            UPDATE identity.refresh_token t
               SET revoked_at = t.revoked_at - interval '1 minute'
              FROM identity.app_user u
             WHERE u.id = t.user_id AND u.email = @email AND t.revoked_at IS NOT NULL
            """,
            ("email", email));

        var reused = await RefreshAsync(client, original);
        var legit = await RefreshAsync(client, RefreshCookieOf(rotated));

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        // Sinal de roubo: ate a sessao legitima morre, e todo mundo precisa logar de novo.
        Assert.Equal(HttpStatusCode.Unauthorized, legit.StatusCode);
    }

    [Fact]
    public async Task Refresh_DepoisDoLogout_Responde401()
    {
        var client = CookielessClient();
        var cookie = await LoginCookieAsync(client);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", $"refreshToken={cookie}");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);

        // Logout nao ganha a janela de tolerancia: token sem sucessor morre na hora.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, cookie)).StatusCode);
    }

    [Fact]
    public async Task Login_AcimaDoLimitePorMinuto_Responde429()
    {
        await using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPerMinute"] = "3",
        });

        var client = factory.CreateClient();
        HttpResponseMessage? last = null;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            last = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email = "ninguem@teste.local", password = "qualquer-coisa-123" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.Equal("https://financemove.app/errors/rate-limit", await ProblemTypeAsync(last));
    }

    // -----------------------------------------------------------------------------------------

    private static object NewUser(string password = Password, string? inviteCode = null) => new
    {
        name = "Convidado",
        email = $"{Guid.NewGuid():N}@teste.local",
        password,
        inviteCode,
    };

    private HttpClient CookielessClient() => fixture.CreateCookielessClient();

    private async Task<string> LoginCookieAsync(HttpClient client) => (await LoginCookieWithEmailAsync(client)).Cookie;

    private async Task<(string Cookie, string Email)> LoginCookieWithEmailAsync(HttpClient client)
    {
        var (_, email) = await fixture.CreateUserAsync(Password);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        return (RefreshCookieOf(login), email);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={cookie}");
        return await client.SendAsync(request);
    }

    private static string RefreshCookieOf(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));

        return header["refreshToken=".Length..header.IndexOf(';', StringComparison.Ordinal)];
    }

    private static async Task<string?> ProblemTypeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("type").GetString();
    }

    private async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
