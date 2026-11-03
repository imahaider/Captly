namespace Captly.Core.Windowing;

/// <summary>A top-level window, as the finder saw it.</summary>
public sealed record WindowInfo(nint Handle, int ProcessId, string Title, PixelRect Bounds)
{
    public bool IsVisible { get; init; } = true;

    /// <summary>Hidden by Windows itself, for example on another virtual desktop.</summary>
    public bool IsCloaked { get; init; }
    public bool IsMinimised { get; init; }
    public bool IsToolWindow { get; init; }

    /// <summary>Dialogs and pop-ups have an owner. A main window does not.</summary>
    public bool HasOwner { get; init; }
    public bool IsForeground { get; init; }
}

/// <summary>
/// Picks the window the video is most likely in, from an app's top-level windows (PLAN.md Phase 7).
/// A title naming the playing media outweighs everything else, so a minimised video window still
/// wins, and the captions hide rather than jump to another window of the same app.
/// </summary>
public static class WindowScoring
{
    public const int MinimumWidth = 160;
    public const int MinimumHeight = 90;

    private const int TitleMatch = 1000;
    private const int Foreground = 100;
    private const int LargestArea = 50;
    private const int Current = 30;
    private const int Minimised = -500;
    private const int ShortestTitle = 3;
    private const long FullHdArea = 1920L * 1080;

    public static bool IsCandidate(WindowInfo window) =>
        window.IsVisible && !window.IsCloaked && !window.IsToolWindow && !window.HasOwner
        && (window.IsMinimised || window.Bounds.Width >= MinimumWidth && window.Bounds.Height >= MinimumHeight);

    /// <summary>
    /// Title match ++, foreground +, area + (up to full HD), minimised −. The window already in
    /// use gets a little extra, so near-ties do not make the captions hop between windows.
    /// </summary>
    public static int Score(WindowInfo window, string? mediaTitle, nint current = 0)
    {
        var score = 0;
        if (TitleMatches(window.Title, mediaTitle)) score += TitleMatch;
        if (window.IsForeground) score += Foreground;
        score += window.IsMinimised ? Minimised : (int)Math.Min(LargestArea, window.Bounds.Area * LargestArea / FullHdArea);
        if (current != 0 && window.Handle == current) score += Current;
        return score;
    }

    /// <summary>The best candidate. Ties go to the first, which is the top of the z-order.</summary>
    public static WindowInfo? Pick(IEnumerable<WindowInfo> windows, string? mediaTitle, nint current = 0) =>
        windows.Where(IsCandidate).MaxBy(w => Score(w, mediaTitle, current));

    /// <summary>Browsers add their own name and the site's, so the media title only has to appear in the window title.</summary>
    public static bool TitleMatches(string title, string? mediaTitle)
    {
        var wanted = mediaTitle?.Trim();
        return wanted is { Length: >= ShortestTitle } && title.Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }
}
