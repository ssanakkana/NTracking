namespace NTracking.Core.Models;

public sealed record UserIntentInferenceResponse(
    string PredictedIntent,
    string Explanation,
    double? Confidence,
    string ModelName,
    string RawResponseJson);