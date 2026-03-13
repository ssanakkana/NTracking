namespace NTracking.Core.Models;

public sealed record UserIntentInferenceRequest(
    string SessionId,
    string? CurrentProcessName,
    string? CurrentWindowTitle,
    IReadOnlyList<InferenceSignal> RecentSignals);