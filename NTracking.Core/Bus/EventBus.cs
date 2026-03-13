using NTracking.Core.Abstractions;
using NTracking.Core.Models;
using System.Collections.Concurrent;

namespace NTracking.Core.Bus;

public sealed class EventBus : IEventBus
{    
    private readonly ConcurrentDictionary<Type, ConcurrentBag<Func<EventBase, CancellationToken, ValueTask>>> _handlers = new();

    public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : EventBase
    {
        ArgumentNullException.ThrowIfNull(handler);

        Type eventType = typeof(TEvent);

        Func<EventBase, CancellationToken, ValueTask> wrapper = (evt, ct) =>
            handler.HandleAsync((TEvent)evt, ct);

        ConcurrentBag<Func<EventBase, CancellationToken, ValueTask>> bucket =
            _handlers.GetOrAdd(eventType, static _ => new ConcurrentBag<Func<EventBase, CancellationToken, ValueTask>>());

        bucket.Add(wrapper);
    }

    public async ValueTask PublishAsync<TEvent>(TEvent evt, CancellationToken ct) where TEvent : EventBase
    {
        ArgumentNullException.ThrowIfNull(evt);

        var eventType = typeof(TEvent);
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            foreach (var handler in handlers)
            {
                ct.ThrowIfCancellationRequested();
                await handler(evt, ct).ConfigureAwait(false);
            }
        }
    }
}