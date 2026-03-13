namespace NTracking.Infrastructure.Storage;

public sealed class EventBatchWriter
{
    private readonly EventRepository repository;
    private readonly int flushCount;
    private readonly List<StoredEvent> buffer = new List<StoredEvent>();

    public EventBatchWriter(EventRepository repository, int flushCount = 50)
    {
        this.repository = repository;
        this.flushCount = flushCount;
    }

    public void Enqueue(StoredEvent item)
    {
        lock (buffer)
        {
            buffer.Add(item);
            if (buffer.Count < flushCount)
            {
                return;
            }
        }

        Flush();
    }

    public void Flush()
    {
        List<StoredEvent> snapshot;

        lock (buffer)
        {
            if (buffer.Count == 0)
            {
                return;
            }

            snapshot = new List<StoredEvent>(buffer);
            buffer.Clear();
        }

        repository.InsertBatch(snapshot);
    }
}
