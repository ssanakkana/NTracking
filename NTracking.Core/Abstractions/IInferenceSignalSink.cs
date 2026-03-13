using NTracking.Core.Models;

namespace NTracking.Core.Abstractions;

public interface IInferenceSignalSink
{
    void Enqueue(InferenceSignal signal);

    IAsyncEnumerable<InferenceSignal> ReadAllAsync(CancellationToken ct);
}