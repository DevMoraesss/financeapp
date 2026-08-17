using FinanceMove.Modules.Accounts.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceMove.Modules.Accounts;

/// <summary>Ponto unico de registro do modulo Contas na injecao de dependencia.</summary>
public static class AccountsModuleExtensions
{
    public static IServiceCollection AddAccountsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccountsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AccountsDbContext.Schema)));

        services.AddScoped<IAccountsQuery, AccountsQuery>();
        services.AddScoped<IAccountsService, AccountsService>();

        return services;
    }
}
