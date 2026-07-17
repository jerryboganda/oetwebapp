using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace OetWithDrHesham.Api.Services.Writing.Events;

/// <summary>
/// Lightweight in-process event bus for Writing Module V2. Handlers register
/// themselves through DI as <see cref="IWritingEventHandler{TEvent}"/> scoped
/// implementations. The bus opens its own service scope per dispatch so each
/// handler sees a fresh <see cref="LearnerDbContext"/>.
///
/// Pattern mirrors the simple lookup-by-handler-type approach used elsewhere
/// in the codebase (no MediatR dependency). Handlers are invoked sequentially
/// and exceptions in one handler are logged but do not stop subsequent
/// handlers from running.
/// </summary>
public interface IWritingEventBus
{
    /// <summary>Publish an event to all registered handlers. Returns when all
    /// handlers have completed (or thrown).</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : WritingEvent;
}

/// <summary>Handler contract for one Writing event type. One handler may
/// implement multiple <c>IWritingEventHandler&lt;T&gt;</c> closures.</summary>
public interface IWritingEventHandler<TEvent>
    where TEvent : WritingEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

public sealed class WritingEventBus(IServiceScopeFactory scopeFactory, ILogger<WritingEventBus> logger) : IWritingEventBus
{
    private static readonly ConcurrentDictionary<Type, Type> HandlerInterfaceCache = new();

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : WritingEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        var handlerInterface = HandlerInterfaceCache.GetOrAdd(typeof(TEvent),
            t => typeof(IWritingEventHandler<>).MakeGenericType(t));

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerInterface).ToList();
        if (handlers.Count == 0) return;

        foreach (var handler in handlers)
        {
            if (handler is null) continue;
            try
            {
                var typed = (IWritingEventHandler<TEvent>)handler;
                await typed.HandleAsync(@event, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Writing event handler {HandlerType} failed for {EventType} user={UserId}",
                    handler.GetType().FullName, typeof(TEvent).Name, @event.UserId);
            }
        }
    }
}
