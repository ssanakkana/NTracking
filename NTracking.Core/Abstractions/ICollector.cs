namespace NTracking.Core.Abstractions;

public interface ICollector
{
    string Name { get; }

    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);
}