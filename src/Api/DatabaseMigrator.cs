using FinanceMove.Modules.Accounts;
using FinanceMove.Modules.Budget;
using FinanceMove.Modules.Identity;
using FinanceMove.Modules.Transactions;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Api;

/// <summary>
/// Aplica as migrations de todos os modulos quando a API sobe (liga com Database:MigrateOnStartup).
/// </summary>
/// <remarks>
/// E o que faz o deploy no Railway atualizar o banco sozinho, sem ninguem rodar dotnet ef na mao
/// contra producao. Se uma migration falhar, a API nao sobe, o healthcheck falha e o Railway
/// mantem a versao anterior no ar: o deploy quebrado nunca chega ao usuario.
/// <para>
/// Pressupoe UMA instancia da API (o plano do Railway usado aqui). Com varias replicas, a
/// migration precisaria sair daqui para um passo de pre-deploy.
/// </para>
/// </remarks>
internal static class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator));

        // ORDEM IMPORTA: Identity primeiro, porque as tabelas dos outros modulos tem chave
        // estrangeira para identity.app_user (modelo-de-dados secao 1.1).
        DbContext[] contexts =
        [
            provider.GetRequiredService<IdentityModuleDbContext>(),
            provider.GetRequiredService<AccountsDbContext>(),
            provider.GetRequiredService<TransactionsDbContext>(),
            provider.GetRequiredService<BudgetDbContext>(),
        ];

        foreach (var context in contexts)
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pending.Count == 0)
            {
                continue;
            }

            logger.LogInformation(
                "Aplicando {Count} migration(s) de {Context}: {Migrations}",
                pending.Count,
                context.GetType().Name,
                string.Join(", ", pending));

            await context.Database.MigrateAsync(cancellationToken);
        }
    }
}
