using FinanceMove.Modules.Accounts;
using FinanceMove.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace FinanceMove.IntegrationTests;

/// <summary>
/// Sobe um PostgreSQL de verdade em container e aplica as migrations nele.
/// </summary>
/// <remarks>
/// Por que não usar banco em memória: o FinanceMove depende de coisas que só o Postgres real tem -
/// <c>numeric(14,2)</c>, CHECK constraints, índice único parcial e <c>ON DELETE CASCADE</c> entre
/// schemas. Um fake em memória passaria em testes que quebrariam em produção.
/// <para>O container é criado uma vez por execução e descartado ao final.</para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Mesma imagem do docker-compose.yml: testar contra outra versão do Postgres seria testar
    // um banco que não é o seu.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("financemove_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Cliente HTTP apontando para a API em memória, ligada ao banco do container.</summary>
    public HttpClient Client => _factory!.CreateClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // ORDEM IMPORTA: accounts.account tem FK para identity.app_user, então o schema de
        // identidade precisa existir primeiro (modelo-de-dados secao 1.1).
        await using (var identity = CreateIdentityContext())
        {
            await identity.Database.MigrateAsync();
        }

        await using (var accounts = CreateAccountsContext())
        {
            await accounts.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = ConnectionString,
                }));
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
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
}

/// <summary>
/// Faz todos os testes de integração compartilharem UM container - subir um Postgres por classe
/// de teste deixaria a suíte lenta sem ganho nenhum.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
