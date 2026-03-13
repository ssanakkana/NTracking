namespace NTracking.Core.Models;

public sealed record ResourceSampleEvent : EventBase
{
    public double CpuPercent { get; init; }

    public double WorkingSetMb { get; init; }

    public double PrivateMb { get; init; }
}