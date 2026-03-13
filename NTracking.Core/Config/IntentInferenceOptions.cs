namespace NTracking.Core.Config;

public sealed class IntentInferenceOptions
{
    public const string SectionName = "IntentInference";

    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = "http://127.0.0.1:11434/v1/chat/completions";

    public string? ApiKey { get; init; }

    public string ModelName { get; init; } = "qwen2.5:7b-instruct";

    public int MaxContextEvents { get; init; } = 64;

    public double TriggerConfidence { get; init; } = 0.75d;

    public string SystemPrompt { get; init; } = "你是一个实时用户意图推理器。你会根据最近事件推测用户下一步最可能的高层意图。只输出 JSON，格式为 {\"predictedIntent\":string,\"explanation\":string,\"confidence\":number}。predictedIntent 应当简洁、具体、可执行，confidence 取值 0 到 1。";
}