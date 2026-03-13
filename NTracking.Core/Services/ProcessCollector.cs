using System.Diagnostics;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;

namespace NTracking.Core.Services;

public sealed class ProcessCollector : ICollector
{
    private readonly IEventBus eventBus;
    private readonly TimeSpan pollInterval;
    private readonly Dictionary<int, ProcessSnapshot> tracked = new();
    private readonly object gate = new();
    private readonly string sessionId = Guid.CreateVersion7().ToString("N");

    private CancellationTokenSource? loopCts;
    private Task? loopTask;

    public ProcessCollector(IEventBus eventBus, TimeSpan? pollInterval = null)
    {
        this.eventBus = eventBus;
        this.pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    public string Name => "ProcessCollector";

    public Task StartAsync(CancellationToken ct)
    {
        lock (gate)
        {
            if (loopTask is not null)
            {
                return Task.CompletedTask;
            }

            SeedCurrentProcesses();

            loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            loopTask = RunLoopAsync(loopCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Task? taskToAwait;

        lock (gate)
        {
            if (loopTask is null)
            {
                return;
            }

            loopCts?.Cancel();
            taskToAwait = loopTask;
            loopTask = null;
        }

        if (taskToAwait is not null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        loopCts?.Dispose();
        loopCts = null;
    }

    private void SeedCurrentProcesses()
    {
        tracked.Clear();

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                tracked[process.Id] = new ProcessSnapshot(
                    process.Id,
                    process.ProcessName,
                    TryGetExecutablePath(process),
                    DateTime.UtcNow);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(pollInterval);

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            Dictionary<int, ProcessSnapshot> current = SnapshotProcesses();
            DateTime now = DateTime.UtcNow;

            List<ProcessSnapshot> started = new();
            List<ProcessSnapshot> exited = new();

            lock (gate)
            {
                Dictionary<int, ProcessSnapshot> previous = new(tracked);

                foreach ((int pid, ProcessSnapshot snapshot) in current)
                {
                    if (!previous.ContainsKey(pid))
                    {
                        started.Add(snapshot);
                    }
                }

                foreach ((int pid, ProcessSnapshot snapshot) in previous)
                {
                    if (!current.ContainsKey(pid))
                    {
                        exited.Add(snapshot);
                    }
                }

                tracked.Clear();
                foreach ((int pid, ProcessSnapshot snapshot) in current)
                {
                    if (previous.TryGetValue(pid, out ProcessSnapshot? oldSnapshot))
                    {
                        tracked[pid] = snapshot with { StartedAtUtc = oldSnapshot.StartedAtUtc };
                    }
                    else
                    {
                        tracked[pid] = snapshot;
                    }
                }
            }

            foreach (ProcessSnapshot snapshot in started)
            {
                ProcessEvent evt = new()
                {
                    Source = Name,
                    SessionId = sessionId,
                    ProcessId = snapshot.ProcessId,
                    ProcessName = snapshot.ProcessName,
                    ExecutablePath = snapshot.ExecutablePath,
                    Action = ProcessEventAction.Started,
                    DurationMs = null,
                    OccurredAtUtc = now,
                };

                await eventBus.PublishAsync(evt, ct).ConfigureAwait(false);
            }

            foreach (ProcessSnapshot snapshot in exited)
            {
                long durationMs = Math.Max(0L, (long)(now - snapshot.StartedAtUtc).TotalMilliseconds);

                ProcessEvent evt = new()
                {
                    Source = Name,
                    SessionId = sessionId,
                    ProcessId = snapshot.ProcessId,
                    ProcessName = snapshot.ProcessName,
                    ExecutablePath = snapshot.ExecutablePath,
                    Action = ProcessEventAction.Exited,
                    DurationMs = durationMs,
                    OccurredAtUtc = now,
                };

                await eventBus.PublishAsync(evt, ct).ConfigureAwait(false);
            }
        }
    }

    private static Dictionary<int, ProcessSnapshot> SnapshotProcesses()
    {
        Dictionary<int, ProcessSnapshot> result = new();
        DateTime now = DateTime.UtcNow;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                result[process.Id] = new ProcessSnapshot(
                    process.Id,
                    process.ProcessName,
                    TryGetExecutablePath(process),
                    now);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ProcessSnapshot(
        int ProcessId,
        string ProcessName,
        string? ExecutablePath,
        DateTime StartedAtUtc);
}