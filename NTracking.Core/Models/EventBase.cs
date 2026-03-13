namespace NTracking.Core.Models;

public abstract record EventBase()
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    public required string Source { get; init; }

    public required string SessionId { get; init; }

    public int PayloadVersion { get; init; } = 1;
}