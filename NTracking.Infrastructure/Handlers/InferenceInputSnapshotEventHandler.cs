using System.Text.Json;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;

namespace NTracking.Infrastructure.Handlers;

public sealed class InferenceInputSnapshotEventHandler : IEventHandler<InputSnapshotEvent>
{
    private readonly IInferenceSignalSink signalSink;

    public InferenceInputSnapshotEventHandler(IInferenceSignalSink signalSink)
    {
        this.signalSink = signalSink;
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

        signalSink.Enqueue(new InferenceSignal(
            evt.EventId.ToString("N"),
            evt.SessionId,
            evt.OccurredAtUtc,
            "input_snapshot",
            evt.Source,
            $"用户在 {evt.ProcessName} / {evt.WindowTitle} 输入或确认文本: {evt.SnapshotText}",
            payloadJson,
            evt.ProcessName,
            evt.WindowTitle));

        return ValueTask.CompletedTask;
    }
}