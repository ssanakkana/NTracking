using System.Threading.Channels;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;

namespace NTracking.Core.Inference;

public sealed class InferenceSignalQueue : IInferenceSignalSink
{
    private readonly Channel<InferenceSignal> channel = Channel.CreateUnbounded<InferenceSignal>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public void Enqueue(InferenceSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        channel.Writer.TryWrite(signal);
    }

    public IAsyncEnumerable<InferenceSignal> ReadAllAsync(CancellationToken ct)
    {
        return channel.Reader.ReadAllAsync(ct);
    }
}