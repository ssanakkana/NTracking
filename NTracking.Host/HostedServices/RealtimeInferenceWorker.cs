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
        await foreach (InferenceSignal signal in signalSink.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            IntentInferenceOptions options = optionsMonitor.CurrentValue;
            InferenceSessionState state = GetOrCreateState(signal.SessionId);
            state.Apply(signal, options.MaxContextEvents);

            if (!options.Enabled)
            {
                continue;
            }

            try
            {
                UserIntentInferenceResponse response = await inferenceClient
                    .InferAsync(state.BuildRequest(), stoppingToken)
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

                if ((response.Confidence ?? 0d) >= options.TriggerConfidence)
                {
                    logger.LogInformation(
                        "Intent trigger: {Intent} confidence={Confidence:F2} session={SessionId}",
                        response.PredictedIntent,
                        response.Confidence ?? 0d,
                        signal.SessionId);
                    continue;
                }

                logger.LogDebug(
                    "Intent updated without trigger: {Intent} confidence={Confidence:F2} session={SessionId}",
                    response.PredictedIntent,
                    response.Confidence ?? 0d,
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