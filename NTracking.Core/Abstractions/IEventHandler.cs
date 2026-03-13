using NTracking.Core.Models;

namespace NTracking.Core.Abstractions;

public interface IEventHandler<TEvent> where TEvent : EventBase
{
    ValueTask HandleAsync(TEvent evt, CancellationToken ct);
}