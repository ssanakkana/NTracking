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
    private readonly IEnumerable<IEventHandler<ProcessEvent>> processEventHandlers;
    private readonly IEnumerable<IEventHandler<WindowEvent>> windowEventHandlers;
    private readonly IEnumerable<IEventHandler<InputSnapshotEvent>> inputSnapshotEventHandlers;
    private readonly SchemaInitializer schemaInitializer;
    private readonly EventBatchWriter batchWriter;
    private readonly ILogger<TrackingWorker> logger;

    public TrackingWorker(
        IEnumerable<ICollector> collectors,
        IEventBus eventBus,
        IEnumerable<IEventHandler<ProcessEvent>> processEventHandlers,
        IEnumerable<IEventHandler<WindowEvent>> windowEventHandlers,
        IEnumerable<IEventHandler<InputSnapshotEvent>> inputSnapshotEventHandlers,
        SchemaInitializer schemaInitializer,
        EventBatchWriter batchWriter,
        ILogger<TrackingWorker> logger)
    {
        this.collectors = collectors;
        this.eventBus = eventBus;
        this.processEventHandlers = processEventHandlers;
        this.windowEventHandlers = windowEventHandlers;
        this.inputSnapshotEventHandlers = inputSnapshotEventHandlers;
        this.schemaInitializer = schemaInitializer;
        this.batchWriter = batchWriter;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        schemaInitializer.Initialize();

        foreach (IEventHandler<ProcessEvent> handler in processEventHandlers)
        {
            eventBus.Subscribe(handler);
        }

        foreach (IEventHandler<WindowEvent> handler in windowEventHandlers)
        {
            eventBus.Subscribe(handler);
        }

        foreach (IEventHandler<InputSnapshotEvent> handler in inputSnapshotEventHandlers)
        {
            eventBus.Subscribe(handler);
        }

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