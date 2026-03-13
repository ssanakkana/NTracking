using NTracking.Core.Models;

namespace NTracking.Core.Abstractions;

public interface IEventBus
{
    void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : EventBase;

    ValueTask PublishAsync<TEvent>(TEvent evt, CancellationToken ct) where TEvent : EventBase;
}