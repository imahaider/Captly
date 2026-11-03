using Captly.Core.Audio;
using Captly.Core.Windowing;
using Microsoft.Extensions.Logging;

namespace Captly.Windowing;

/// <summary>
/// Follows the video's window (PLAN.md Phase 7). WinEvent hooks on the app's process report
/// moves, resizes, minimising and closing as they happen, so the captions keep up during a drag.
/// A 4 Hz poll catches anything the hooks miss, such as another window moving over the video, and
/// every 2 s the windows are scored again, in case the video moved to another window.
/// Create it and call it on the UI thread: hooks are delivered through that thread's messages.
/// </summary>
public sealed class WindowTracker : IWindowTracker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(2);

    private static readonly (uint Min, uint Max)[] ProcessEvents =
    [
        (Native.EventSystemMoveSizeStart, Native.EventSystemMoveSizeEnd),
        (Native.EventSystemMinimizeStart, Native.EventSystemMinimizeEnd),
        (Native.EventObjectDestroy, Native.EventObjectHide),
        (Native.EventObjectLocationChange, Native.EventObjectNameChange),
        (Native.EventObjectCloaked, Native.EventObjectUncloaked),
    ];

    private readonly Func<ProcessTree> _processes;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _context;
    private readonly int _excludedPid;
    private readonly Timer _timer;
    private readonly Native.WinEventProc _onEvent;
    private readonly List<nint> _processHooks = [];
    private nint _foregroundHook;
    private int? _rootPid;
    private string? _title;
    private nint _window;
    private int _hookedPid;
    private long _lastSearch;
    private bool _searchWanted;
    private int _tickQueued;
    private bool _disposed;

    /// <param name="processes">A process snapshot, for matching windows to the app's process tree.</param>
    /// <param name="excludedPid">Windows of this process never count as covering the video. By default, Captly's own.</param>
    public WindowTracker(Func<ProcessTree> processes, ILogger<WindowTracker> logger, int? excludedPid = null)
        : this(processes, logger, excludedPid ?? Environment.ProcessId,
            SynchronizationContext.Current ?? throw new InvalidOperationException("Create the window tracker on the UI thread."))
    {
    }

    internal WindowTracker(Func<ProcessTree> processes, ILogger logger, int excludedPid, SynchronizationContext context)
    {
        _processes = processes;
        _logger = logger;
        _excludedPid = excludedPid;
        _context = context;
        _onEvent = OnEvent;
        _timer = new Timer(_ => QueueTick());
    }

    public WindowPlacement? Current { get; private set; }

    public event Action<WindowPlacement?>? PlacementChanged;

    public event Action? ForegroundChanged;

    public void Track(int? rootPid, string? titleHint)
    {
        if (_disposed || rootPid == _rootPid && titleHint == _title) return;
        _rootPid = rootPid;
        _title = titleHint;

        if (rootPid == null)
        {
            Stop();
            return;
        }

        if (_foregroundHook == 0)
        {
            _foregroundHook = Native.SetWinEventHook(Native.EventSystemForeground, Native.EventSystemForeground, 0, _onEvent, 0, 0,
                Native.WinEventOutOfContext | Native.WinEventSkipOwnProcess);
        }
        _timer.Change(PollInterval, PollInterval);
        Search();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Dispose();
    }

    /// <summary>What the poll does: score the windows again every 2 s, or when asked to, otherwise re-read the placement.</summary>
    internal void Tick()
    {
        if (_disposed || _rootPid == null) return;
        if (_searchWanted || Environment.TickCount64 - _lastSearch >= SearchInterval.TotalMilliseconds) Search();
        else Refresh();
    }

    private void Search()
    {
        _searchWanted = false;
        _lastSearch = Environment.TickCount64;
        if (_rootPid is not { } root) return;

        var tree = _processes();
        var best = WindowScoring.Pick(WindowFinder.List(pid => pid != 0 && tree.RootOf(pid) == root), _title, _window);
        var handle = best?.Handle ?? 0;
        if (handle != _window)
        {
            _window = handle;
            HookProcess(best?.ProcessId ?? 0);
            if (best == null) _logger.LogInformation("No window to pin captions to in process {Pid}", root);
            else _logger.LogInformation("Captions pinned to window \"{Title}\", {Width}x{Height}", best.Title, best.Bounds.Width, best.Bounds.Height);
        }
        Refresh();
    }

    private void Refresh()
    {
        if (_window != 0 && !WindowFinder.Exists(_window))
        {
            _window = 0;
            Search();
            return;
        }
        Publish(_window == 0 ? null : WindowFinder.Placement(_window, _excludedPid));
    }

    // Nothing is raised once disposed, because listeners may already be gone.
    private void Publish(WindowPlacement? placement)
    {
        if (placement == null ? Current == null : placement.SameAs(Current)) return;
        Current = placement;
        if (!_disposed) PlacementChanged?.Invoke(placement);
    }

    private void Stop()
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        HookProcess(0);
        if (_foregroundHook != 0) Native.UnhookWinEvent(_foregroundHook);
        _foregroundHook = 0;
        _window = 0;
        Publish(null);
    }

    // Hooks only the window's own process, so the rest of the desktop sends nothing.
    private void HookProcess(int pid)
    {
        if (pid == _hookedPid) return;
        foreach (var hook in _processHooks) Native.UnhookWinEvent(hook);
        _processHooks.Clear();
        _hookedPid = pid;
        if (pid == 0) return;

        foreach (var (min, max) in ProcessEvents)
        {
            var hook = Native.SetWinEventHook(min, max, 0, _onEvent, pid, 0, Native.WinEventOutOfContext);
            if (hook != 0) _processHooks.Add(hook);
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_disposed || _rootPid == null) return;

        if (eventType == Native.EventSystemForeground)
        {
            ForegroundChanged?.Invoke();
            Search();
            return;
        }

        if (idObject != Native.ObjIdWindow || idChild != 0) return;

        // A new tab or title may point to another window. Batched into the next poll.
        if (eventType == Native.EventObjectNameChange)
        {
            _searchWanted = true;
            return;
        }

        if (hwnd != _window) return;
        if (eventType is Native.EventObjectDestroy or Native.EventObjectHide) Search();
        else Refresh();
    }

    // The timer fires on the thread pool. At most one tick waits on the UI thread at a time.
    private void QueueTick()
    {
        if (Interlocked.Exchange(ref _tickQueued, 1) != 0) return;
        _context.Post(_ =>
        {
            Volatile.Write(ref _tickQueued, 0);
            Tick();
        }, null);
    }
}
