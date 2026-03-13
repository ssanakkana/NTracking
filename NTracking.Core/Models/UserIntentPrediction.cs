namespace NTracking.Core.Models;

public sealed record UserIntentPrediction
{
    public Guid PredictionId { get; init; } = Guid.CreateVersion7();

    public required string SessionId { get; init; }

    public DateTime PredictedAtUtc { get; init; } = DateTime.UtcNow;

    public required string TriggerEventId { get; init; }

    public string? CurrentProcessName { get; init; }

    public string? CurrentWindowTitle { get; init; }

    public required string PredictedIntent { get; init; }

    public required string Explanation { get; init; }

    public double? Confidence { get; init; }

    public required string ModelName { get; init; }

    public required string RawResponseJson { get; init; }
}