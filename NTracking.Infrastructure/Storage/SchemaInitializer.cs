using Microsoft.Data.Sqlite;

namespace NTracking.Infrastructure.Storage;

public sealed class SchemaInitializer
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SchemaInitializer(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public void Initialize()
    {
        using SqliteConnection connection = connectionFactory.CreateOpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS Events (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId TEXT NOT NULL,
    EventType TEXT NOT NULL,
    OccurredAtUtc TEXT NOT NULL,
    Source TEXT NOT NULL,
    SessionId TEXT NOT NULL,
    PayloadJson TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Events_OccurredAtUtc ON Events(OccurredAtUtc);
CREATE INDEX IF NOT EXISTS IX_Events_EventType ON Events(EventType);
CREATE INDEX IF NOT EXISTS IX_Events_SessionId ON Events(SessionId);";
        command.ExecuteNonQuery();
    }
}
