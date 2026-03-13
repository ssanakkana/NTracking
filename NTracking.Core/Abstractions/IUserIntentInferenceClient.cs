using NTracking.Core.Models;

namespace NTracking.Core.Abstractions;

public interface IUserIntentInferenceClient
{
    Task<UserIntentInferenceResponse> InferAsync(UserIntentInferenceRequest request, CancellationToken ct);
}