using Microsoft.Data.Sqlite;
using NTracking.Core.Models;

namespace NTracking.Infrastructure.Storage;

public sealed class IntentPredictionRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public IntentPredictionRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public void Insert(UserIntentPrediction prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        using SqliteConnection connection = connectionFactory.CreateOpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO IntentPredictions (
    PredictionId,
    SessionId,
    PredictedAtUtc,
    TriggerEventId,
    CurrentProcessName,
    CurrentWindowTitle,
    PredictedIntent,
    Explanation,
    Confidence,
    ModelName,
    RawResponseJson)
VALUES (
    $predictionId,
    $sessionId,
    $predictedAtUtc,
    $triggerEventId,
    $currentProcessName,
    $currentWindowTitle,
    $predictedIntent,
    $explanation,
    $confidence,
    $modelName,
    $rawResponseJson);";

        command.Parameters.AddWithValue("$predictionId", prediction.PredictionId.ToString("N"));
        command.Parameters.AddWithValue("$sessionId", prediction.SessionId);
        command.Parameters.AddWithValue("$predictedAtUtc", prediction.PredictedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$triggerEventId", prediction.TriggerEventId);
        command.Parameters.AddWithValue("$currentProcessName", (object?)prediction.CurrentProcessName ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentWindowTitle", (object?)prediction.CurrentWindowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$predictedIntent", prediction.PredictedIntent);
        command.Parameters.AddWithValue("$explanation", prediction.Explanation);
        command.Parameters.AddWithValue("$confidence", (object?)prediction.Confidence ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelName", prediction.ModelName);
        command.Parameters.AddWithValue("$rawResponseJson", prediction.RawResponseJson);
        command.ExecuteNonQuery();
    }
}