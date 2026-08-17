using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace FinanceMove.Api;

/// <summary>
/// Materializa as recorrencias vencidas na primeira requisicao autenticada do usuario no dia
/// (modelo-de-dados secao 4.2).
/// </summary>
/// <remarks>
/// Sem isso, a conta de luz cadastrada como recorrente so viraria transacao quando alguem
/// rodasse um job. Rodar aqui e barato: a marca em memoria evita repetir na mesma data, e a
/// propria rotina e idempotente (usa FOR UPDATE SKIP LOCKED), entao mesmo em duas abas abertas
/// ao mesmo tempo nada duplica.
/// </remarks>
internal sealed class RecurrenceCatchUpMiddleware(RequestDelegate next, IMemoryCache cache)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IRecurrenceService recurrences,
        IClock clock,
        ILogger<RecurrenceCatchUpMiddleware> logger)
    {
        if (currentUser.IsAuthenticated)
        {
            var key = $"catchup:{currentUser.Id}:{clock.Today:yyyy-MM-dd}";

            if (!cache.TryGetValue(key, out _))
            {
                cache.Set(key, true, TimeSpan.FromHours(12));

                try
                {
                    var created = await recurrences.RunCatchUpAsync(currentUser.Id, context.RequestAborted);

                    if (created > 0)
                    {
                        logger.LogInformation("Catch-up criou {Count} lancamento(s) pendente(s).", created);
                    }
                }
                catch (Exception exception)
                {
                    // Falha no catch-up nao pode derrubar a requisicao do usuario. Ele tenta de
                    // novo amanha, ou quando a marca em memoria expirar.
                    cache.Remove(key);
                    logger.LogError(exception, "Catch-up de recorrencia falhou.");
                }
            }
        }

        await next(context);
    }
}
