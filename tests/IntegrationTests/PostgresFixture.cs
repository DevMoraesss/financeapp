using System.Net.Http.Json;
using FinanceMove.Modules.Accounts;
using FinanceMove.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FinanceMove.IntegrationTests;

/// <summary>
/// Sobe um PostgreSQL de verdade e aplica as migrations nele.
/// </summary>
/// <remarks>
/// Por que não usar banco em memória: o FinanceMove depende de coisas que só o Postgres real tem -
/// <c>numeric(14,2)</c>, CHECK constraints, índice único parcial e <c>ON DELETE CASCADE</c> entre
/// schemas. Um fake em memória passaria em testes que quebrariam em produção.
/// <para>
/// Dois jeitos de conseguir o Postgres: por padrão, um container do Testcontainers (precisa de
/// Docker). Se a variável <c>FINANCEMOVE_TEST_POSTGRES</c> apontar para um servidor existente, o
/// fixture cria nele um banco descartável com nome aleatório e o apaga no fim - útil em máquina
/// sem Docker.
/// </para>
/// <para>
/// As migrations rodam pelo mesmo <c>DatabaseMigrator</c> que o deploy usa
/// (Database:MigrateOnStartup), então a suíte também prova que o banco de produção sobe do zero.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string? _externalServer = Environment.GetEnvironmentVariable("FINANCEMOVE_TEST_POSTGRES");

    private PostgreSqlContainer? _container;
    private string? _externalDatabase;
    private WebApplicationFactory<Program>? _factory;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Cliente HTTP apontando para a API em memória, ligada ao banco de teste.</summary>
    public HttpClient Client => _factory!.CreateClient();

    /// <summary>Cliente sem pote de cookies: o teste escolhe qual cookie vai em cada chamada.</summary>
    public HttpClient CreateCookielessClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_externalServer))
        {
            var admin = new NpgsqlConnectionStringBuilder(_externalServer);
            _externalDatabase = $"financemove_test_{Guid.NewGuid():N}";

            await using (var connection = new NpgsqlConnection(admin.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_externalDatabase}\"", connection);
                await command.ExecuteNonQueryAsync();
            }

            admin.Database = _externalDatabase;
            ConnectionString = admin.ConnectionString;
        }
        else
        {
            // Mesma imagem do docker-compose.yml: testar contra outra versão do Postgres seria
            // testar um banco que não é o seu.
            _container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("financemove_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        _factory = CreateFactory(new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "true" });

        // Força o host a subir agora: é nessa hora que as migrations dos quatro módulos rodam.
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (_externalDatabase is not null)
        {
            NpgsqlConnection.ClearAllPools();

            await using var connection = new NpgsqlConnection(_externalServer);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_externalDatabase}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Uma API em memória ligada ao banco de teste, com configuração extra por cima da de
    /// desenvolvimento. Serve para testar comportamento que depende de configuração (convite,
    /// limite de usuários, rate limit) sem mexer na API compartilhada pelos outros testes.
    /// </summary>
    /// <remarks>Quem cria é dono: descarte a factory no fim do teste.</remarks>
    public WebApplicationFactory<Program> CreateFactory(IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,

            // A suíte cria dezenas de usuários e lançamentos pelo mesmo "IP" do TestServer; com
            // o limite de produção, os testes brigariam com o rate limit em vez de testar regra.
            ["RateLimiting:GlobalPerMinute"] = "100000",
            ["RateLimiting:AuthPerMinute"] = "100000",
            ["RateLimiting:SessionPerMinute"] = "100000",
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            // UseSetting, e nao so ConfigureAppConfiguration: o Program.cs le a connection string
            // ANTES do Build, quando a configuracao extra do teste ainda nao foi aplicada. Sem
            // isto a API de teste ia parar no banco de desenvolvimento (porta 5435).
            builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });
    }

    /// <summary>Cadastra e loga um usuário novo pela API. Devolve o cliente já autenticado.</summary>
    public async Task<(HttpClient Client, string Email)> CreateUserAsync(string password = "senhaDeTeste123")
    {
        var client = Client;
        var email = $"{Guid.NewGuid():N}@teste.local";

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new { name = "Teste", email, password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var token = (await login.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return (client, email);
    }

    public IdentityModuleDbContext CreateIdentityContext() =>
        new(new DbContextOptionsBuilder<IdentityModuleDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityModuleDbContext.Schema))
            .Options);

    public AccountsDbContext CreateAccountsContext() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AccountsDbContext.Schema))
            .Options);

    private sealed record LoginBody(string AccessToken);
}

/// <summary>
/// Faz todos os testes de integração compartilharem UM banco - subir um Postgres por classe
/// de teste deixaria a suíte lenta sem ganho nenhum.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
