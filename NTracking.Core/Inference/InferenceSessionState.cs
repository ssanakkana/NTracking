using NTracking.Core.Models;

namespace NTracking.Core.Inference;

public sealed class InferenceSessionState
{
    private readonly List<InferenceSignal> recentSignals = new();

    public string SessionId { get; }

    public string? CurrentProcessName { get; private set; }

    public string? CurrentWindowTitle { get; private set; }

    public InferenceSessionState(string sessionId)
    {
        SessionId = sessionId;
    }

    public void Apply(InferenceSignal signal, int maxContextEvents)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (!string.IsNullOrWhiteSpace(signal.ProcessName))
        {
            CurrentProcessName = signal.ProcessName;
        }

        if (!string.IsNullOrWhiteSpace(signal.WindowTitle))
        {
            CurrentWindowTitle = signal.WindowTitle;
        }

        recentSignals.Add(signal);
        int overflow = recentSignals.Count - Math.Max(1, maxContextEvents);
        if (overflow > 0)
        {
            recentSignals.RemoveRange(0, overflow);
        }
    }

    public UserIntentInferenceRequest BuildRequest()
    {
        return new UserIntentInferenceRequest(
            SessionId,
            CurrentProcessName,
            CurrentWindowTitle,
            recentSignals.ToArray());
    }
}