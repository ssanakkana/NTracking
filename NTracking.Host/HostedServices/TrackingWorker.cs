using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;
using NTracking.Infrastructure.Storage;

namespace NTracking.Host.HostedServices;

public sealed class TrackingWorker : IHostedService
{
    private readonly IEnumerable<ICollector> collectors;
    private readonly IEventBus eventBus;
    private readonly IEventHandler<ProcessEvent> processEventHandler;
    private readonly IEventHandler<WindowEvent> windowEventHandler;
    private readonly IEventHandler<InputSnapshotEvent> inputSnapshotEventHandler;
    private readonly SchemaInitializer schemaInitializer;
    private readonly EventBatchWriter batchWriter;
    private readonly ILogger<TrackingWorker> logger;

    public TrackingWorker(
        IEnumerable<ICollector> collectors,
        IEventBus eventBus,
        IEventHandler<ProcessEvent> processEventHandler,
        IEventHandler<WindowEvent> windowEventHandler,
        IEventHandler<InputSnapshotEvent> inputSnapshotEventHandler,
        SchemaInitializer schemaInitializer,
        EventBatchWriter batchWriter,
        ILogger<TrackingWorker> logger)
    {
        this.collectors = collectors;
        this.eventBus = eventBus;
        this.processEventHandler = processEventHandler;
        this.windowEventHandler = windowEventHandler;
        this.inputSnapshotEventHandler = inputSnapshotEventHandler;
        this.schemaInitializer = schemaInitializer;
        this.batchWriter = batchWriter;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        schemaInitializer.Initialize();
        eventBus.Subscribe(processEventHandler);
        eventBus.Subscribe(windowEventHandler);
        eventBus.Subscribe(inputSnapshotEventHandler);

        foreach (ICollector collector in collectors)
        {
            logger.LogInformation("Starting collector: {Collector}", collector.Name);
            await collector.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (ICollector collector in collectors.Reverse())
        {
            try
            {
                logger.LogInformation("Stopping collector: {Collector}", collector.Name);
                await collector.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        batchWriter.Flush();
    }
}