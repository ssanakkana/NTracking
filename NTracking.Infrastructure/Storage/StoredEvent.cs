namespace NTracking.Infrastructure.Storage;

public sealed record StoredEvent(
    string EventId,
    string EventType,
    DateTime OccurredAtUtc,
    string Source,
    string SessionId,
    string PayloadJson);
