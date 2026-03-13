namespace NTracking.Core.Models;

public sealed record WindowEvent : EventBase
{
    public required string ProcessName { get; init; }

    public required string WindowTitle { get; init; }

    public required string ClassName { get; init; }

    public bool IsSwitch { get; init; }
}