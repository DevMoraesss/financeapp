using Microsoft.Extensions.DependencyInjection;

namespace FinanceMove.Shared.Events;

/// <summary>Marcador de evento de dominio.</summary>
public interface IDomainEvent;

/// <summary>Quem reage a um evento. Um evento pode ter varios ouvintes, ou nenhum.</summary>
public interface IEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}

/// <summary>
/// Barramento in-process: resolve os ouvintes pelo container e chama todos, em sequencia.
/// </summary>
/// <remarks>
/// E o que permite ao modulo Identidade publicar UserRegistered sem saber que o modulo
/// Transactions vai criar as categorias do seed (docs/arquitetura.md secao 2, regra 4).
/// <para>
/// Deliberadamente sincrono e sem fila: os ouvintes rodam dentro da MESMA transacao de banco de
/// quem publicou, entao ou tudo grava ou nada grava. Nunca existe usuario sem categorias.
/// Se um dia algum ouvinte precisar ser assincrono de verdade, ai sim entra fila.
/// </para>
/// </remarks>
public sealed class InProcessEventBus(IServiceProvider services) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
        {
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }
}
