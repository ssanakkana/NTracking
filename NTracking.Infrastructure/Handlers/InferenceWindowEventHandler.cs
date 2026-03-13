using System.Text.Json;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;

namespace NTracking.Infrastructure.Handlers;

public sealed class InferenceWindowEventHandler : IEventHandler<WindowEvent>
{
    private readonly IInferenceSignalSink signalSink;

    public InferenceWindowEventHandler(IInferenceSignalSink signalSink)
    {
        this.signalSink = signalSink;
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

        signalSink.Enqueue(new InferenceSignal(
            evt.EventId.ToString("N"),
            evt.SessionId,
            evt.OccurredAtUtc,
            "window",
            evt.Source,
            $"窗口切换到 {evt.ProcessName} / {evt.WindowTitle}",
            payloadJson,
            evt.ProcessName,
            evt.WindowTitle));

        return ValueTask.CompletedTask;
    }
}