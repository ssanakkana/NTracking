using System.Diagnostics;
using System.Windows.Automation;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;

namespace NTracking.Core.Services;

public sealed class InputSnapshotCollector : ICollector
{
    private const int SearchTimeoutMs = 80;
    private const int SearchMaxNodes = 200;

    private readonly IEventBus eventBus;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan idleTimeout;
    private readonly string sessionId = Guid.CreateVersion7().ToString("N");

    private readonly object stateLock = new();

    private SnapshotState? current;
    private CancellationTokenSource? loopCts;
    private Task? loopTask;

    private AutomationElement? cachedCaretControl;
    private Task<SearchResult>? searchTask;
    private int searchCooldownTicks;

    public InputSnapshotCollector(
        IEventBus eventBus,
        TimeSpan? pollInterval = null,
        TimeSpan? idleTimeout = null)
    {
        this.eventBus = eventBus;
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        this.idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(3);
    }

    public string Name => "InputSnapshotCollector";

    public Task StartAsync(CancellationToken ct)
    {
        if (loopTask is not null)
        {
            return Task.CompletedTask;
        }

        Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
        Automation.AddAutomationEventHandler(
            TextPattern.TextSelectionChangedEvent,
            AutomationElement.RootElement,
            TreeScope.Subtree,
            OnTextSelectionChanged);

        loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loopTask = RunLoopAsync(loopCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (loopTask is null)
        {
            return;
        }

        loopCts?.Cancel();

        try
        {
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (current is not null)
        {
            await PublishSnapshotAsync(current, InputSnapshotTriggerReason.ForceFlush, CancellationToken.None)
                .ConfigureAwait(false);
            current = null;
        }

        Automation.RemoveAutomationFocusChangedEventHandler(OnFocusChanged);
        Automation.RemoveAutomationEventHandler(
            TextPattern.TextSelectionChangedEvent,
            AutomationElement.RootElement,
            OnTextSelectionChanged);

        loopCts?.Dispose();
        loopCts = null;
        loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(pollInterval);

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            CaretProbe? probe = TryGetCaretProbe();
            if (probe is null)
            {
                continue;
            }

            DateTime now = DateTime.UtcNow;

            if (current is null)
            {
                if (!string.IsNullOrWhiteSpace(probe.ControlText))
                {
                    current = SnapshotState.FromProbe(probe, now);
                }

                continue;
            }

            bool sameControl = string.Equals(current.ControlKey, probe.ControlKey, StringComparison.Ordinal);
            if (!sameControl)
            {
                InputSnapshotTriggerReason reason = IsWindowSwitch(current, probe)
                    ? InputSnapshotTriggerReason.WindowSwitch
                    : InputSnapshotTriggerReason.FocusLost;

                await PublishSnapshotAsync(current, reason, ct).ConfigureAwait(false);
                current = string.IsNullOrWhiteSpace(probe.ControlText)
                    ? null
                    : SnapshotState.FromProbe(probe, now);
                continue;
            }

            // Requirement #1: for the same caret control, only keep the latest text.
            if (!string.Equals(current.SnapshotText, probe.ControlText, StringComparison.Ordinal))
            {
                current = current with
                {
                    SnapshotText = probe.ControlText,
                    LastChangedAtUtc = now,
                };
                continue;
            }

            if (now - current.LastChangedAtUtc >= idleTimeout)
            {
                await PublishSnapshotAsync(current, InputSnapshotTriggerReason.IdleTimeout, ct).ConfigureAwait(false);
                current = current with { LastChangedAtUtc = now };
            }
        }
    }

    private async ValueTask PublishSnapshotAsync(
        SnapshotState state,
        InputSnapshotTriggerReason reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.SnapshotText))
        {
            return;
        }

        InputSnapshotEvent evt = new()
        {
            Source = Name,
            SessionId = sessionId,
            ProcessName = state.ProcessName,
            WindowTitle = state.WindowTitle,
            ControlType = state.ControlType,
            ControlName = state.ControlName,
            SnapshotText = state.SnapshotText,
            TriggerReason = reason,
            OccurredAtUtc = DateTime.UtcNow,
        };

        await eventBus.PublishAsync(evt, ct).ConfigureAwait(false);
    }

    private CaretProbe? TryGetCaretProbe()
    {
        AutomationElement? caretControl = GetActiveCaretControl();
        if (caretControl is null)
        {
            return null;
        }

        string controlText = GetControlText(caretControl);
        if (string.IsNullOrWhiteSpace(controlText))
        {
            return null;
        }

        int processId = TryGetProcessId(caretControl);
        string processName = TryGetProcessName(processId);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        string windowTitle = GetWindowTitle(caretControl);
        string controlType = GetControlType(caretControl);
        string controlName = SafeGetElementName(caretControl);
        string controlKey = BuildControlKey(caretControl, processId, windowTitle, controlType, controlName);

        return new CaretProbe(
            controlKey,
            processId,
            processName,
            windowTitle,
            controlType,
            controlName,
            controlText);
    }

    private static bool IsWindowSwitch(SnapshotState state, CaretProbe probe)
    {
        return state.ProcessId != probe.ProcessId
            || !string.Equals(state.WindowTitle, probe.WindowTitle, StringComparison.Ordinal);
    }

    private AutomationElement? GetActiveCaretControl()
    {
        SearchResult? completedResult = TryConsumeCompletedSearch();
        if (completedResult?.Element is not null)
        {
            SetCachedCaretControl(completedResult.Element);
        }

        AutomationElement? cached = GetCachedCaretControl();
        if (cached is not null && HasActiveCaret(cached))
        {
            return cached;
        }

        AutomationElement? focusedElement;
        try
        {
            focusedElement = AutomationElement.FocusedElement;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        if (focusedElement is null)
        {
            return null;
        }

        if (HasActiveCaret(focusedElement))
        {
            SetCachedCaretControl(focusedElement);
            return focusedElement;
        }

        lock (stateLock)
        {
            if (searchCooldownTicks > 0)
            {
                searchCooldownTicks--;
                return null;
            }
        }

        StartSearchIfNeeded(focusedElement);

        lock (stateLock)
        {
            searchCooldownTicks = 2;
        }

        return null;
    }

    private SearchResult ResolveCaretControlWithTimeout(AutomationElement focusedElement)
    {
        DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(SearchTimeoutMs);
        int visited = 0;
        Queue<AutomationElement> queue = new();
        queue.Enqueue(focusedElement);

        try
        {
            while (queue.Count > 0 && visited < SearchMaxNodes)
            {
                if (DateTime.UtcNow >= deadlineUtc)
                {
                    return new SearchResult(null, true);
                }

                AutomationElement currentElement = queue.Dequeue();
                visited++;

                if (IsCandidate(currentElement) && HasActiveCaret(currentElement))
                {
                    return new SearchResult(currentElement, false);
                }

                EnqueueChildren(currentElement, queue, deadlineUtc);
            }

            return new SearchResult(null, false);
        }
        catch (ElementNotAvailableException)
        {
            return new SearchResult(null, false);
        }
        catch (InvalidOperationException)
        {
            return new SearchResult(null, false);
        }
    }

    private static bool IsCandidate(AutomationElement element)
    {
        try
        {
            object controlType = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
            if (controlType is ControlType type && type == ControlType.Edit)
            {
                return true;
            }

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textObj) && textObj is TextPattern)
            {
                return true;
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObj) && valueObj is ValuePattern)
            {
                return true;
            }
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        return false;
    }

    private static void EnqueueChildren(AutomationElement element, Queue<AutomationElement> queue, DateTime deadlineUtc)
    {
        TreeWalker walker = TreeWalker.ControlViewWalker;
        AutomationElement? child;

        try
        {
            child = walker.GetFirstChild(element);
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        while (child is not null)
        {
            if (DateTime.UtcNow >= deadlineUtc)
            {
                return;
            }

            queue.Enqueue(child);

            try
            {
                child = walker.GetNextSibling(child);
            }
            catch (ElementNotAvailableException)
            {
                return;
            }
        }
    }

    private void StartSearchIfNeeded(AutomationElement focusedElement)
    {
        lock (stateLock)
        {
            if (searchTask is { IsCompleted: false })
            {
                return;
            }

            searchTask = Task.Run(() => ResolveCaretControlWithTimeout(focusedElement));
        }
    }

    private SearchResult? TryConsumeCompletedSearch()
    {
        Task<SearchResult>? completedTask;

        lock (stateLock)
        {
            if (searchTask is null || !searchTask.IsCompleted)
            {
                return null;
            }

            completedTask = searchTask;
            searchTask = null;
        }

        try
        {
            return completedTask.Result;
        }
        catch
        {
            return null;
        }
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs _)
    {
        if (sender is not AutomationElement element)
        {
            return;
        }

        if (HasActiveCaret(element))
        {
            SetCachedCaretControl(element);
            return;
        }

        StartSearchIfNeeded(element);
    }

    private void OnTextSelectionChanged(object sender, AutomationEventArgs _)
    {
        if (sender is AutomationElement element && HasActiveCaret(element))
        {
            SetCachedCaretControl(element);
        }
    }

    private AutomationElement? GetCachedCaretControl()
    {
        lock (stateLock)
        {
            return cachedCaretControl;
        }
    }

    private void SetCachedCaretControl(AutomationElement element)
    {
        lock (stateLock)
        {
            cachedCaretControl = element;
            searchCooldownTicks = 0;
        }
    }

    private static bool HasActiveCaret(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textObj) && textObj is TextPattern textPattern)
            {
                return textPattern.GetSelection().Length > 0;
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObj) && valueObj is ValuePattern)
            {
                return element.Current.HasKeyboardFocus;
            }
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        return false;
    }

    private static int TryGetProcessId(AutomationElement element)
    {
        try
        {
            return element.Current.ProcessId;
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
    }

    private static string TryGetProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowTitle(AutomationElement element)
    {
        AutomationElement? currentElement = element;
        TreeWalker walker = TreeWalker.ControlViewWalker;

        while (currentElement is not null)
        {
            try
            {
                object controlType = currentElement.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
                if (controlType is ControlType type && type == ControlType.Window)
                {
                    return NormalizeText(currentElement.Current.Name);
                }

                currentElement = walker.GetParent(currentElement);
            }
            catch (ElementNotAvailableException)
            {
                break;
            }
        }

        return string.Empty;
    }

    private static string GetControlType(AutomationElement element)
    {
        try
        {
            object controlType = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
            return controlType is ControlType type ? type.ProgrammaticName : "Unknown";
        }
        catch (ElementNotAvailableException)
        {
            return "Unknown";
        }
    }

    private static string BuildControlKey(
        AutomationElement element,
        int processId,
        string windowTitle,
        string controlType,
        string controlName)
    {
        try
        {
            int[] runtimeId = element.GetRuntimeId();
            if (runtimeId.Length > 0)
            {
                return string.Join('-', runtimeId);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        return $"{processId}|{windowTitle}|{controlType}|{controlName}";
    }

    private static string GetControlText(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObj) && valueObj is ValuePattern valuePattern)
            {
                return NormalizeText(valuePattern.Current.Value);
            }

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textObj) && textObj is TextPattern textPattern)
            {
                return NormalizeText(textPattern.DocumentRange.GetText(-1));
            }

            return NormalizeText(SafeGetElementName(element));
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static string SafeGetElementName(AutomationElement element)
    {
        try
        {
            return element.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string normalized = value.Replace("\r", "").Replace("\n", " ").Trim();
        const int maxLength = 2000;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private sealed record CaretProbe(
        string ControlKey,
        int ProcessId,
        string ProcessName,
        string WindowTitle,
        string ControlType,
        string ControlName,
        string ControlText);

    private sealed record SnapshotState(
        string ControlKey,
        int ProcessId,
        string ProcessName,
        string WindowTitle,
        string ControlType,
        string ControlName,
        string SnapshotText,
        DateTime LastChangedAtUtc)
    {
        public static SnapshotState FromProbe(CaretProbe probe, DateTime utcNow)
        {
            return new SnapshotState(
                probe.ControlKey,
                probe.ProcessId,
                probe.ProcessName,
                probe.WindowTitle,
                probe.ControlType,
                probe.ControlName,
                probe.ControlText,
                utcNow);
        }
    }

    private sealed class SearchResult
    {
        public SearchResult(AutomationElement? element, bool timedOut)
        {
            Element = element;
            TimedOut = timedOut;
        }

        public AutomationElement? Element { get; }
        public bool TimedOut { get; }
    }
}
