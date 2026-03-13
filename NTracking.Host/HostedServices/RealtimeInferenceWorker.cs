using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NTracking.Core.Abstractions;
using NTracking.Core.Config;
using NTracking.Core.Inference;
using NTracking.Core.Models;
using NTracking.Infrastructure.Storage;

namespace NTracking.Host.HostedServices;

public sealed class RealtimeInferenceWorker : BackgroundService
{
    private readonly IInferenceSignalSink signalSink;
    private readonly IUserIntentInferenceClient inferenceClient;
    private readonly IntentPredictionRepository predictionRepository;
    private readonly IOptionsMonitor<IntentInferenceOptions> optionsMonitor;
    private readonly ILogger<RealtimeInferenceWorker> logger;
    private readonly Dictionary<string, InferenceSessionState> sessionStates = new(StringComparer.Ordinal);
    private bool disabledStateLogged;

    public RealtimeInferenceWorker(
        IInferenceSignalSink signalSink,
        IUserIntentInferenceClient inferenceClient,
        IntentPredictionRepository predictionRepository,
        IOptionsMonitor<IntentInferenceOptions> optionsMonitor,
        ILogger<RealtimeInferenceWorker> logger)
    {
        this.signalSink = signalSink;
        this.inferenceClient = inferenceClient;
        this.predictionRepository = predictionRepository;
        this.optionsMonitor = optionsMonitor;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IntentInferenceOptions startupOptions = optionsMonitor.CurrentValue;
        logger.LogInformation(
            "Realtime inference worker started. Enabled={Enabled} Model={ModelName} MaxContextEvents={MaxContextEvents} TriggerConfidence={TriggerConfidence:F2}",
            startupOptions.Enabled,
            startupOptions.ModelName,
            startupOptions.MaxContextEvents,
            startupOptions.TriggerConfidence);

        await foreach (InferenceSignal signal in signalSink.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            IntentInferenceOptions options = optionsMonitor.CurrentValue;
            InferenceSessionState state = GetOrCreateState(signal.SessionId);
            state.Apply(signal, options.MaxContextEvents);

            logger.LogInformation(
                "Inference signal received. EventType={EventType} SessionId={SessionId} EventId={EventId} ContextEvents={ContextEvents} Process={ProcessName} Window={WindowTitle}",
                signal.EventType,
                signal.SessionId,
                signal.EventId,
                state.BuildRequest().RecentSignals.Count,
                state.CurrentProcessName ?? "<null>",
                state.CurrentWindowTitle ?? "<null>");

            if (!options.Enabled)
            {
                if (!disabledStateLogged)
                {
                    logger.LogWarning(
                        "Intent inference is disabled. Signals are still collected, but no prediction request will be sent. Set IntentInference:Enabled=true to turn it on.");
                    disabledStateLogged = true;
                }

                continue;
            }

            disabledStateLogged = false;

            try
            {
                UserIntentInferenceRequest request = state.BuildRequest();
                logger.LogInformation(
                    "Sending inference request. SessionId={SessionId} ContextEvents={ContextEvents} Process={ProcessName} Window={WindowTitle}",
                    request.SessionId,
                    request.RecentSignals.Count,
                    request.CurrentProcessName ?? "<null>",
                    request.CurrentWindowTitle ?? "<null>");

                UserIntentInferenceResponse response = await inferenceClient
                    .InferAsync(request, stoppingToken)
                    .ConfigureAwait(false);

                UserIntentPrediction prediction = new()
                {
                    SessionId = signal.SessionId,
                    TriggerEventId = signal.EventId,
                    CurrentProcessName = state.CurrentProcessName,
                    CurrentWindowTitle = state.CurrentWindowTitle,
                    PredictedIntent = response.PredictedIntent,
                    Explanation = response.Explanation,
                    Confidence = response.Confidence,
                    ModelName = response.ModelName,
                    RawResponseJson = response.RawResponseJson,
                };

                predictionRepository.Insert(prediction);

                bool isTriggered = (response.Confidence ?? 0d) >= options.TriggerConfidence;
                logger.LogInformation(
                    "Prediction stored. Triggered={Triggered} Intent={Intent} Confidence={Confidence} SessionId={SessionId} Explanation={Explanation}",
                    isTriggered,
                    response.PredictedIntent,
                    response.Confidence?.ToString("F2") ?? "<null>",
                    signal.SessionId,
                    response.Explanation);

                if (isTriggered)
                {
                    logger.LogInformation(
                        "Intent trigger: {Intent} confidence={Confidence:F2} session={SessionId}",
                        response.PredictedIntent,
                        response.Confidence ?? 0d,
                        signal.SessionId);
                    continue;
                }

                logger.LogInformation(
                    "Prediction below trigger threshold. Intent={Intent} confidence={Confidence} threshold={Threshold:F2} session={SessionId}",
                    response.PredictedIntent,
                    response.Confidence?.ToString("F2") ?? "<null>",
                    options.TriggerConfidence,
                    signal.SessionId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Realtime inference failed for session {SessionId}", signal.SessionId);
            }
        }
    }

    private InferenceSessionState GetOrCreateState(string sessionId)
    {
        if (!sessionStates.TryGetValue(sessionId, out InferenceSessionState? state))
        {
            state = new InferenceSessionState(sessionId);
            sessionStates.Add(sessionId, state);
        }

        return state;
    }
}