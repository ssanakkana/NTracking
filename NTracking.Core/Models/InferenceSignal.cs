namespace NTracking.Core.Models;

public sealed record InferenceSignal(
    string EventId,
    string SessionId,
    DateTime OccurredAtUtc,
    string EventType,
    string Source,
    string Summary,
    string PayloadJson,
    string? ProcessName = null,
    string? WindowTitle = null);