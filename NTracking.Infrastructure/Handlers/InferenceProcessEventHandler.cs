using System.Text.Json;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;

namespace NTracking.Infrastructure.Handlers;

public sealed class InferenceProcessEventHandler : IEventHandler<ProcessEvent>
{
    private readonly IInferenceSignalSink signalSink;

    public InferenceProcessEventHandler(IInferenceSignalSink signalSink)
    {
        this.signalSink = signalSink;
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

        string action = evt.Action switch
        {
            ProcessEventAction.Started => "启动",
            ProcessEventAction.Exited => "退出",
            _ => evt.Action.ToString(),
        };

        signalSink.Enqueue(new InferenceSignal(
            evt.EventId.ToString("N"),
            evt.SessionId,
            evt.OccurredAtUtc,
            "process",
            evt.Source,
            $"进程{action}: {evt.ProcessName}",
            payloadJson,
            evt.ProcessName,
            null));

        return ValueTask.CompletedTask;
    }
}