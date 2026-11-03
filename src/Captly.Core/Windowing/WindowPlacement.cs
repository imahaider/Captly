namespace Captly.Core.Windowing;

/// <summary>Where the tracked window is now, in physical pixels.</summary>
public sealed record WindowPlacement(nint Handle, PixelRect Bounds, MonitorInfo Monitor)
{
    public bool IsMinimised { get; init; }

    /// <summary>Not shown: invisible, or cloaked on another virtual desktop.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Windows above this one in the z-order, which could cover the captions.</summary>
    public IReadOnlyList<PixelRect> Above { get; init; } = [];

    /// <summary>Fullscreen when the window covers its whole monitor, taskbar included.</summary>
    public bool IsFullscreen => !IsMinimised && Bounds.Contains(Monitor.Bounds);

    /// <summary>Compares every field, including the windows above, so unchanged polls raise nothing.</summary>
    public bool SameAs(WindowPlacement? other) =>
        other != null && other.Handle == Handle && other.Bounds == Bounds && other.Monitor == Monitor
        && other.IsMinimised == IsMinimised && other.IsHidden == IsHidden && other.Above.SequenceEqual(Above);
}

/// <summary>Follows the video's window so the captions can sit on it.</summary>
public interface IWindowTracker : IDisposable
{
    /// <summary>
    /// Follows the best window of this app, using the media title as a hint. Null stops tracking.
    /// Calling again with the same values does nothing.
    /// </summary>
    void Track(int? rootPid, string? titleHint);

    WindowPlacement? Current { get; }

    /// <summary>Raised on the UI thread. Null means there is no window to pin to.</summary>
    event Action<WindowPlacement?>? PlacementChanged;

    /// <summary>Raised when any app comes to the front, so the overlay can stay on top.</summary>
    event Action? ForegroundChanged;
}
