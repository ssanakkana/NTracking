using System.Text.Json;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;
using NTracking.Infrastructure.Storage;

namespace NTracking.Infrastructure.Handlers;

public sealed class WindowEventHandler : IEventHandler<WindowEvent>
{
    private const string WindowEventType = "window";

    private readonly EventBatchWriter batchWriter;

    public WindowEventHandler(EventBatchWriter batchWriter)
    {
        this.batchWriter = batchWriter;
    }

    public ValueTask HandleAsync(WindowEvent evt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string payloadJson = JsonSerializer.Serialize(new
        {
            evt.ProcessName,
            evt.WindowTitle,
            evt.ClassName,
            evt.IsSwitch,
        });

        StoredEvent storedEvent = new(
            evt.EventId.ToString("N"),
            WindowEventType,
            evt.OccurredAtUtc,
            evt.Source,
            evt.SessionId,
            payloadJson);

        batchWriter.Enqueue(storedEvent);
        return ValueTask.CompletedTask;
    }
}