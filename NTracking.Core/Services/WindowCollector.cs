using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NTracking.Core.Abstractions;
using NTracking.Core.Models;

namespace NTracking.Core.Services;

public sealed class WindowCollector : ICollector
{
    private readonly IEventBus eventBus;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan dedupWindow;
    private readonly string sessionId = Guid.CreateVersion7().ToString("N");

    private WindowSnapshot? lastSnapshot;
    private DateTime lastEmittedAtUtc;
    private CancellationTokenSource? loopCts;
    private Task? loopTask;

    public WindowCollector(IEventBus eventBus, TimeSpan? pollInterval = null, TimeSpan? dedupWindow = null)
    {
        this.eventBus = eventBus;
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(300);
        this.dedupWindow = dedupWindow ?? TimeSpan.FromSeconds(1);
    }

    public string Name => "WindowCollector";

    public Task StartAsync(CancellationToken ct)
    {
        if (loopTask is not null)
        {
            return Task.CompletedTask;
        }

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

        loopCts?.Dispose();
        loopCts = null;
        loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(pollInterval);

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            WindowSnapshot? current = TryGetForegroundWindowSnapshot();
            if (current is null)
            {
                continue;
            }

            DateTime now = DateTime.UtcNow;
            bool switched = lastSnapshot is null || !WindowSnapshot.EqualsForSwitch(lastSnapshot, current);

            if (!switched && now - lastEmittedAtUtc < dedupWindow)
            {
                continue;
            }

            // TODO: consider emitting an event even if the window hasn't switched, if enough time has passed. This can help capture long-running windows that the user is actively looking at, but which for some reason fail to trigger a switch event (e.g. due to a quirk in GetForegroundWindow or related APIs).
            if (switched)
            {
                WindowEvent evt = new()
                {
                    Source = Name,
                    SessionId = sessionId,
                    ProcessName = current.ProcessName,
                    WindowTitle = current.WindowTitle,
                    ClassName = current.ClassName,
                    IsSwitch = true,
                    OccurredAtUtc = now,
                };

                await eventBus.PublishAsync(evt, ct).ConfigureAwait(false);
                lastSnapshot = current;
                lastEmittedAtUtc = now;
            }
        }
    }

    private static WindowSnapshot? TryGetForegroundWindowSnapshot()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        uint processId;
        _ = GetWindowThreadProcessId(hwnd, out processId);
        if (processId == 0)
        {
            return null;
        }

        string title = GetWindowText(hwnd);
        string className = GetClassName(hwnd);
        string processName = TryGetProcessName((int)processId);

        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return new WindowSnapshot((int)processId, processName, title, className);
    }

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowText(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new(length + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        StringBuilder sb = new(256);
        int copied = GetClassName(hwnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private sealed record WindowSnapshot(int ProcessId, string ProcessName, string WindowTitle, string ClassName)
    {
        public static bool EqualsForSwitch(WindowSnapshot left, WindowSnapshot right)
        {
            return left.ProcessId == right.ProcessId
                && string.Equals(left.WindowTitle, right.WindowTitle, StringComparison.Ordinal)
                && string.Equals(left.ClassName, right.ClassName, StringComparison.Ordinal);
        }
    }
}