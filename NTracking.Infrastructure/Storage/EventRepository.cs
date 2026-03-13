using Microsoft.Data.Sqlite;

namespace NTracking.Infrastructure.Storage;

public sealed class EventRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public EventRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public void InsertBatch(IReadOnlyList<StoredEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        using SqliteConnection connection = connectionFactory.CreateOpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO Events (EventId, EventType, OccurredAtUtc, Source, SessionId, PayloadJson)
VALUES ($eventId, $eventType, $occurredAtUtc, $source, $sessionId, $payloadJson);";

        SqliteParameter eventId = command.Parameters.Add("$eventId", SqliteType.Text);
        SqliteParameter eventType = command.Parameters.Add("$eventType", SqliteType.Text);
        SqliteParameter occurredAtUtc = command.Parameters.Add("$occurredAtUtc", SqliteType.Text);
        SqliteParameter source = command.Parameters.Add("$source", SqliteType.Text);
        SqliteParameter sessionId = command.Parameters.Add("$sessionId", SqliteType.Text);
        SqliteParameter payloadJson = command.Parameters.Add("$payloadJson", SqliteType.Text);

        foreach (StoredEvent item in events)
        {
            eventId.Value = item.EventId;
            eventType.Value = item.EventType;
            occurredAtUtc.Value = item.OccurredAtUtc.ToString("O");
            source.Value = item.Source;
            sessionId.Value = item.SessionId;
            payloadJson.Value = item.PayloadJson;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<StoredEvent> QueryByTimeRange(DateTime fromUtc, DateTime toUtc, string? eventType = null)
    {
        using SqliteConnection connection = connectionFactory.CreateOpenConnection();
        using SqliteCommand command = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(eventType))
        {
            command.CommandText = @"
SELECT EventId, EventType, OccurredAtUtc, Source, SessionId, PayloadJson
FROM Events
WHERE OccurredAtUtc >= $fromUtc AND OccurredAtUtc <= $toUtc
ORDER BY OccurredAtUtc;";
        }
        else
        {
            command.CommandText = @"
SELECT EventId, EventType, OccurredAtUtc, Source, SessionId, PayloadJson
FROM Events
WHERE OccurredAtUtc >= $fromUtc AND OccurredAtUtc <= $toUtc AND EventType = $eventType
ORDER BY OccurredAtUtc;";
            command.Parameters.AddWithValue("$eventType", eventType);
        }

        command.Parameters.AddWithValue("$fromUtc", fromUtc.ToString("O"));
        command.Parameters.AddWithValue("$toUtc", toUtc.ToString("O"));

        using SqliteDataReader reader = command.ExecuteReader();
        List<StoredEvent> result = new List<StoredEvent>();

        while (reader.Read())
        {
            result.Add(new StoredEvent(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return result;
    }
}
