namespace NTracking.Core.Models;

public enum ProcessEventAction
{
    Started = 1,
    Exited = 2
}

public sealed record ProcessEvent : EventBase
{
    public int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public string? ExecutablePath { get; init; }

    public ProcessEventAction Action { get; init; }

    public long? DurationMs { get; init; }
}