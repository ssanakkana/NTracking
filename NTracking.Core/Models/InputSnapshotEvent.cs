namespace NTracking.Core.Models;

public enum InputSnapshotTriggerReason
{
    FocusLost = 1,
    IdleTimeout = 2,
    WindowSwitch = 3,
    ForceFlush = 4
}

public sealed record InputSnapshotEvent : EventBase
{
    public required string ProcessName { get; init; }

    public required string WindowTitle { get; init; }

    public required string ControlType { get; init; }

    public required string ControlName { get; init; }

    public required string SnapshotText { get; init; }

    public InputSnapshotTriggerReason TriggerReason { get; init; }
}