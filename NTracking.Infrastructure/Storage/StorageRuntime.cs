namespace NTracking.Infrastructure.Storage;

public sealed class StorageRuntime
{
    public StorageRuntime(
        SqliteConnectionFactory connectionFactory,
        SchemaInitializer schemaInitializer,
        EventRepository eventRepository,
        EventBatchWriter batchWriter)
    {
        ConnectionFactory = connectionFactory;
        SchemaInitializer = schemaInitializer;
        EventRepository = eventRepository;
        BatchWriter = batchWriter;
    }

    public SqliteConnectionFactory ConnectionFactory { get; }
    public SchemaInitializer SchemaInitializer { get; }
    public EventRepository EventRepository { get; }
    public EventBatchWriter BatchWriter { get; }

    public static StorageRuntime Create(StorageOptions options, int flushCount = 50)
    {
        SqliteConnectionFactory connectionFactory = new SqliteConnectionFactory(options);
        SchemaInitializer schemaInitializer = new SchemaInitializer(connectionFactory);
        EventRepository repository = new EventRepository(connectionFactory);
        EventBatchWriter batchWriter = new EventBatchWriter(repository, flushCount);
        return new StorageRuntime(connectionFactory, schemaInitializer, repository, batchWriter);
    }
}
