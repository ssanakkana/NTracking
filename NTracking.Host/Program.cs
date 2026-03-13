using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NTracking.Core.Abstractions;
using NTracking.Core.Bus;
using NTracking.Core.Models;
using NTracking.Core.Services;
using NTracking.Host.HostedServices;
using NTracking.Infrastructure.Storage;
using NTracking.Infrastructure.Handlers;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IEventBus, EventBus>();
builder.Services.AddSingleton<ICollector, ProcessCollector>();
builder.Services.AddSingleton<ICollector, WindowCollector>();
builder.Services.AddSingleton<ICollector, InputSnapshotCollector>();

builder.Services.AddSingleton(_ => new StorageOptions
{
	DatabasePath = "ntracking.db",
});
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<SchemaInitializer>();
builder.Services.AddSingleton<EventRepository>();
builder.Services.AddSingleton(sp => new EventBatchWriter(sp.GetRequiredService<EventRepository>(), flushCount: 1));
builder.Services.AddSingleton<IEventHandler<ProcessEvent>, ProcessEventHandler>();
builder.Services.AddSingleton<IEventHandler<WindowEvent>, WindowEventHandler>();
builder.Services.AddSingleton<IEventHandler<InputSnapshotEvent>, InputSnapshotEventHandler>();

builder.Services.AddHostedService<TrackingWorker>();

using IHost host = builder.Build();
await host.RunAsync();
