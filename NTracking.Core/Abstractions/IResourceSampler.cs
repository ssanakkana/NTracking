using NTracking.Core.Models;

namespace NTracking.Core.Abstractions;

public interface IResourceSampler
{
    ValueTask<ResourceSampleEvent> SampleAsync(CancellationToken ct);
}