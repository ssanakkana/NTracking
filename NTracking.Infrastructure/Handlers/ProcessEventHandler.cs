using System.Text.Json;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;
using NTracking.Infrastructure.Storage;

namespace NTracking.Infrastructure.Handlers;

public sealed class ProcessEventHandler : IEventHandler<ProcessEvent>
{
    private const string ProcessEventType = "process";

    private readonly EventBatchWriter batchWriter;

    public ProcessEventHandler(EventBatchWriter batchWriter)
    {
        this.batchWriter = batchWriter;
    }

    public ValueTask HandleAsync(ProcessEvent evt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string payloadJson = JsonSerializer.Serialize(new
        {
            evt.ProcessId,
            evt.ProcessName,
            evt.ExecutablePath,
            Action = evt.Action.ToString(),
            evt.DurationMs,
        });

        StoredEvent storedEvent = new(
            evt.EventId.ToString("N"),
            ProcessEventType,
            evt.OccurredAtUtc,
            evt.Source,
            evt.SessionId,
            payloadJson);

        batchWriter.Enqueue(storedEvent);
        return ValueTask.CompletedTask;
    }
}