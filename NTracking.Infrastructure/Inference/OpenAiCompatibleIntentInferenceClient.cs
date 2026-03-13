using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NTracking.Core.Abstractions;
using NTracking.Core.Config;
using NTracking.Core.Models;

namespace NTracking.Infrastructure.Inference;

public sealed class OpenAiCompatibleIntentInferenceClient : IUserIntentInferenceClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly IOptionsMonitor<IntentInferenceOptions> optionsMonitor;

    public OpenAiCompatibleIntentInferenceClient(HttpClient httpClient, IOptionsMonitor<IntentInferenceOptions> optionsMonitor)
    {
        this.httpClient = httpClient;
        this.optionsMonitor = optionsMonitor;
    }

    public async Task<UserIntentInferenceResponse> InferAsync(UserIntentInferenceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        IntentInferenceOptions options = optionsMonitor.CurrentValue;
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, options.Endpoint);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        string userPrompt = BuildUserPrompt(request);
        var payload = new
        {
            model = options.ModelName,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = options.SystemPrompt },
                new { role = "user", content = userPrompt },
            },
            response_format = new
            {
                type = "json_object",
            },
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        string rawResponseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return ParseResponse(rawResponseJson, options.ModelName);
    }

    private static string BuildUserPrompt(UserIntentInferenceRequest request)
    {
        var promptPayload = new
        {
            sessionId = request.SessionId,
            currentProcessName = request.CurrentProcessName,
            currentWindowTitle = request.CurrentWindowTitle,
            recentSignals = request.RecentSignals.Select(signal => new
            {
                signal.OccurredAtUtc,
                signal.EventType,
                signal.Source,
                signal.ProcessName,
                signal.WindowTitle,
                signal.Summary,
                signal.PayloadJson,
            }),
        };

        return JsonSerializer.Serialize(promptPayload, SerializerOptions);
    }

    private static UserIntentInferenceResponse ParseResponse(string rawResponseJson, string fallbackModelName)
    {
        using JsonDocument document = JsonDocument.Parse(rawResponseJson);
        JsonElement root = document.RootElement;

        string modelName = root.TryGetProperty("model", out JsonElement modelElement)
            ? modelElement.GetString() ?? fallbackModelName
            : fallbackModelName;

        string content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        using JsonDocument contentDocument = JsonDocument.Parse(content);
        JsonElement contentRoot = contentDocument.RootElement;

        string predictedIntent = contentRoot.TryGetProperty("predictedIntent", out JsonElement predictedIntentElement)
            ? predictedIntentElement.GetString() ?? "unknown"
            : "unknown";

        string explanation = contentRoot.TryGetProperty("explanation", out JsonElement explanationElement)
            ? explanationElement.GetString() ?? string.Empty
            : content;

        double? confidence = null;
        if (contentRoot.TryGetProperty("confidence", out JsonElement confidenceElement)
            && confidenceElement.ValueKind is JsonValueKind.Number
            && confidenceElement.TryGetDouble(out double parsedConfidence))
        {
            confidence = parsedConfidence;
        }

        return new UserIntentInferenceResponse(
            predictedIntent,
            explanation,
            confidence,
            modelName,
            rawResponseJson);
    }
}