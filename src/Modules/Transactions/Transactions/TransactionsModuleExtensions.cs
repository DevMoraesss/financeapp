using FinanceMove.Modules.Identity.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceMove.Modules.Transactions;

/// <summary>Ponto unico de registro do modulo Transactions na injecao de dependencia.</summary>
public static class TransactionsModuleExtensions
{
    public static IServiceCollection AddTransactionsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TransactionsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TransactionsDbContext.Schema)));

        services.AddScoped<ITransactionsQuery, TransactionsQuery>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IStatementService, StatementService>();
        services.AddScoped<IRecurrenceService, RecurrenceService>();

        // Ouve o cadastro de usuario para criar as categorias padrao. O modulo Identidade nao
        // sabe que esta linha existe.
        services.AddScoped<IEventHandler<UserRegistered>, CategorySeedHandler>();

        return services;
    }
}
