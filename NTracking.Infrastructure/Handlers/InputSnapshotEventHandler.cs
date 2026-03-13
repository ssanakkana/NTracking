using System.Text.Json;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;
using NTracking.Infrastructure.Storage;

namespace NTracking.Infrastructure.Handlers;

public sealed class InputSnapshotEventHandler : IEventHandler<InputSnapshotEvent>
{
    private const string InputSnapshotEventType = "input_snapshot";

    private readonly EventBatchWriter batchWriter;

    public InputSnapshotEventHandler(EventBatchWriter batchWriter)
    {
        this.batchWriter = batchWriter;
    }

    public ValueTask HandleAsync(InputSnapshotEvent evt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string payloadJson = JsonSerializer.Serialize(new
        {
            evt.ProcessName,
            evt.WindowTitle,
            evt.ControlType,
            evt.ControlName,
            evt.SnapshotText,
            TriggerReason = evt.TriggerReason.ToString(),
        });

        StoredEvent storedEvent = new(
            evt.EventId.ToString("N"),
            InputSnapshotEventType,
            evt.OccurredAtUtc,
            evt.Source,
            evt.SessionId,
            payloadJson);

        batchWriter.Enqueue(storedEvent);
        return ValueTask.CompletedTask;
    }
}