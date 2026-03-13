using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NTracking.Core.Abstractions;
using NTracking.Core.Bus;
using NTracking.Core.Config;
using NTracking.Core.Inference;
using NTracking.Core.Models;
using NTracking.Core.Services;
using NTracking.Host.HostedServices;
using NTracking.Infrastructure.Inference;
using NTracking.Infrastructure.Storage;
using NTracking.Infrastructure.Handlers;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<IntentInferenceOptions>(builder.Configuration.GetSection(IntentInferenceOptions.SectionName));

builder.Services.AddSingleton<IEventBus, EventBus>();
builder.Services.AddSingleton<IInferenceSignalSink, InferenceSignalQueue>();
builder.Services.AddSingleton<ICollector, ProcessCollector>();
builder.Services.AddSingleton<ICollector, WindowCollector>();
builder.Services.AddSingleton<ICollector, InputSnapshotCollector>();
builder.Services.AddSingleton<HttpClient>();

builder.Services.AddSingleton(_ => new StorageOptions
{
	DatabasePath = "ntracking.db",
});
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<SchemaInitializer>();
builder.Services.AddSingleton<EventRepository>();
builder.Services.AddSingleton<IntentPredictionRepository>();
builder.Services.AddSingleton(sp => new EventBatchWriter(sp.GetRequiredService<EventRepository>(), flushCount: 1));
builder.Services.AddSingleton<IEventHandler<ProcessEvent>, ProcessEventHandler>();
builder.Services.AddSingleton<IEventHandler<WindowEvent>, WindowEventHandler>();
builder.Services.AddSingleton<IEventHandler<InputSnapshotEvent>, InputSnapshotEventHandler>();
builder.Services.AddSingleton<IEventHandler<ProcessEvent>, InferenceProcessEventHandler>();
builder.Services.AddSingleton<IEventHandler<WindowEvent>, InferenceWindowEventHandler>();
builder.Services.AddSingleton<IEventHandler<InputSnapshotEvent>, InferenceInputSnapshotEventHandler>();
builder.Services.AddSingleton<IUserIntentInferenceClient, OpenAiCompatibleIntentInferenceClient>();

builder.Services.AddHostedService<TrackingWorker>();
builder.Services.AddHostedService<RealtimeInferenceWorker>();

using IHost host = builder.Build();
await host.RunAsync();
