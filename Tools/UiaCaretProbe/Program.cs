using System.Windows.Automation;

namespace UiaCaretProbe;

public class Program
{
    private const int LoopIntervalMs = 25;
    private const int SearchTimeoutMs = 80;
    private const int SearchMaxNodes = 200;

    private static readonly object _stateLock = new();
    private static readonly Condition _candidateCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true),
        new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true));

    private static string? _lastSnapshot;
    private static volatile bool _isRunning = true;
    private static AutomationElement? _cachedCaretControl;
    private static int _searchCooldownTicks;
    private static Task<SearchResult>? _searchTask;
    private static DateTime _lastTimeoutLogUtc = DateTime.MinValue;

    [STAThread]
    private static void Main()
    {
        Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
        Automation.AddAutomationEventHandler(
            TextPattern.TextSelectionChangedEvent,
            AutomationElement.RootElement,
            TreeScope.Subtree,
            OnTextSelectionChanged);

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _isRunning = false;
        };

        while (_isRunning)
        {
            try
            {
                AutomationElement? caretControl = GetActiveCaretControl();
                if (caretControl != null)
                {
                    string controlName = SafeGetElementName(caretControl);
                    string controlText = GetControlText(caretControl);
                    string snapshot = $"{controlName}|{controlText}";

                    if (!string.Equals(snapshot, _lastSnapshot, StringComparison.Ordinal))
                    {
                        _lastSnapshot = snapshot;
                        Console.WriteLine($"Caret Control: {controlName}, Text: {controlText}");
                    }
                }
            }

            // UI Automation providers may disappear between read operations.
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            Thread.Sleep(LoopIntervalMs);
        }

        Automation.RemoveAutomationFocusChangedEventHandler(OnFocusChanged);
        Automation.RemoveAutomationEventHandler(
            TextPattern.TextSelectionChangedEvent,
            AutomationElement.RootElement,
            OnTextSelectionChanged);

        Console.WriteLine("Stopped.");
    }

    private static AutomationElement? GetActiveCaretControl()
    {
        SearchResult? completedResult = TryConsumeCompletedSearch();
        if (completedResult?.Element != null)
        {
            SetCachedCaretControl(completedResult.Element);
        }

        if (completedResult?.TimedOut == true)
        {
            MaybeLogTimeout();
        }

        AutomationElement? cached = GetCachedCaretControl();
        if (cached != null && HasActiveCaret(cached))
        {
            return cached;
        }

        AutomationElement? focusedElement = null;
        try
        {
            focusedElement = AutomationElement.FocusedElement;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        if (focusedElement == null) return null;

        if (HasActiveCaret(focusedElement))
        {
            SetCachedCaretControl(focusedElement);
            return focusedElement;
        }

        lock (_stateLock)
        {
            if (_searchCooldownTicks > 0)
            {
                _searchCooldownTicks--;
                return null;
            }
        }

        StartSearchIfNeeded(focusedElement);

        lock (_stateLock)
        {
            // Backoff avoids launching a new background search on every tick.
            _searchCooldownTicks = 2;
        }

        return null;
    }

    private static SearchResult ResolveCaretControlWithTimeout(AutomationElement focusedElement)
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

                AutomationElement current = queue.Dequeue();
                visited++;

                if (IsCandidate(current) && HasActiveCaret(current))
                {
                    return new SearchResult(current, false);
                }

                EnqueueChildren(current, queue, deadlineUtc);
            }

            if (DateTime.UtcNow >= deadlineUtc)
            {
                return new SearchResult(null, true);
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

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObj) && textPatternObj is TextPattern)
            {
                return true;
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObj) && valuePatternObj is ValuePattern)
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

        while (child != null)
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

    private static void StartSearchIfNeeded(AutomationElement focusedElement)
    {
        lock (_stateLock)
        {
            if (_searchTask is { IsCompleted: false })
            {
                return;
            }

            _searchTask = Task.Run(() => ResolveCaretControlWithTimeout(focusedElement));
        }
    }

    private static SearchResult? TryConsumeCompletedSearch()
    {
        Task<SearchResult>? completedTask;
        lock (_stateLock)
        {
            if (_searchTask == null || !_searchTask.IsCompleted)
            {
                return null;
            }

            completedTask = _searchTask;
            _searchTask = null;
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

    private static void OnFocusChanged(object sender, AutomationFocusChangedEventArgs _)
    {
        if (sender is not AutomationElement element)
        {
            return;
        }

        try
        {
            if (HasActiveCaret(element))
            {
                SetCachedCaretControl(element);
                return;
            }

            StartSearchIfNeeded(element);
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static void OnTextSelectionChanged(object sender, AutomationEventArgs _)
    {
        if (sender is AutomationElement element && HasActiveCaret(element))
        {
            SetCachedCaretControl(element);
        }
    }

    private static AutomationElement? GetCachedCaretControl()
    {
        lock (_stateLock)
        {
            return _cachedCaretControl;
        }
    }

    private static void SetCachedCaretControl(AutomationElement element)
    {
        lock (_stateLock)
        {
            _cachedCaretControl = element;
            _searchCooldownTicks = 0;
        }
    }

    private static void MaybeLogTimeout()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastTimeoutLogUtc).TotalMilliseconds < 800)
        {
            return;
        }

        _lastTimeoutLogUtc = now;
        Console.WriteLine($"[search-timeout] Caret resolve exceeded {SearchTimeoutMs}ms, continuing realtime loop.");
    }

    private static bool HasActiveCaret(AutomationElement element)
    {
        try
        {
            object? text;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out text) && text is TextPattern textPattern)
            {
                var selection = textPattern.GetSelection();
                return selection.Length > 0;
            }

            object? value;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out value) && value is ValuePattern)
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

    public static string GetControlText(AutomationElement element)
    {
        try
        {
            object? value;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out value) && value is ValuePattern valuePattern)
            {
                return NormalizeText(valuePattern.Current.Value);
            }

            object? text;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out text) && text is TextPattern textPattern)
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
        const int maxLength = 300;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
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
