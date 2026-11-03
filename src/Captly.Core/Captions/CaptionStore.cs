using Captly.Core.Settings;
using Captly.Core.Speech;

namespace Captly.Core.Captions;

public sealed record CaptionOptions
{
    public int MaxLines { get; init; } = 2;
    public int CharsPerLine { get; init; } = 42;
    public TimeSpan ClearAfter { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan MinDisplay { get; init; } = TimeSpan.FromSeconds(1.2);

    public static CaptionOptions From(AppearanceSettings settings) => new()
    {
        MaxLines = settings.MaxLines,
        CharsPerLine = settings.CharsPerLine,
        ClearAfter = TimeSpan.FromMilliseconds(settings.ClearAfterMs),
    };
}

/// <summary>
/// Decides what the overlay shows, following PLAN.md §8. Partials revise the bottom of the display
/// and finals commit. The last lines of a final stay up for the minimum display time before newer
/// text may push them off, unless so much is waiting that captions would fall behind the video.
/// Not thread-safe: use it from one thread and call <see cref="Tick"/> regularly while it has text.
/// </summary>
public sealed class CaptionStore(CaptionOptions options, TimeProvider? clock = null)
{
    private sealed class Line(string text, bool holds)
    {
        public string Text { get; } = text;
        public bool Holds { get; set; } = holds;
        public DateTimeOffset? HeldUntil { get; set; }
    }

    // Lines waiting below the display before holds give way. Captions must not lag the video.
    private const int MaxWaitingScreens = 2;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly List<Line> _committed = [];
    private List<string> _partial = [];
    private IReadOnlyList<string> _visible = [];
    private DateTimeOffset _lastActivity;

    /// <summary>Raised when <see cref="Lines"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The lines to show now, top first. Empty means nothing should be on screen.</summary>
    public IReadOnlyList<string> Lines => _visible;

    public bool HasText => _visible.Count > 0;

    public void Apply(CaptionEvent caption)
    {
        var text = CaptionText.Normalize(caption.Text);
        var lines = CaptionText.Wrap(text, options.CharsPerLine);

        if (caption.IsFinal)
        {
            _partial = [];
            for (var i = 0; i < lines.Count; i++)
                _committed.Add(new Line(lines[i], holds: i >= lines.Count - options.MaxLines));
        }
        else
        {
            _partial = lines;
        }

        if (lines.Count > 0) _lastActivity = _clock.GetUtcNow();
        Refresh();
    }

    /// <summary>Applies timing: releases held lines, and clears after silence.</summary>
    public void Tick()
    {
        Refresh();
        if (!HasText || FirstHeldLine() >= 0 && WaitingBelowDisplay() > 0) return;
        if (_clock.GetUtcNow() - _lastActivity >= options.ClearAfter) Clear();
    }

    /// <summary>Removes everything at once, for a seek or a new video.</summary>
    public void Clear()
    {
        _committed.Clear();
        _partial = [];
        Show([]);
    }

    private void Refresh()
    {
        var start = WindowStart();
        if (start > _committed.Count)
        {
            _partial = _partial[(start - _committed.Count)..];
            start = _committed.Count;
        }
        _committed.RemoveRange(0, start);

        var now = _clock.GetUtcNow();
        foreach (var line in _committed.Take(options.MaxLines))
            if (line.Holds) line.HeldUntil ??= now + options.MinDisplay;

        Show(_committed.Select(l => l.Text).Concat(_partial).Take(options.MaxLines).ToList());
    }

    private int WindowStart()
    {
        var latest = Math.Max(0, _committed.Count + _partial.Count - options.MaxLines);
        var held = FirstHeldLine();
        if (held < 0 || held >= latest) return latest;

        if (latest - held > MaxWaitingScreens * options.MaxLines)
        {
            foreach (var line in _committed) line.Holds = false;
            return latest;
        }
        return held;
    }

    private int WaitingBelowDisplay() => Math.Max(0, _committed.Count + _partial.Count - options.MaxLines);

    private int FirstHeldLine()
    {
        var now = _clock.GetUtcNow();
        return _committed.FindIndex(l => l.Holds && (l.HeldUntil is null || l.HeldUntil > now));
    }

    private void Show(IReadOnlyList<string> lines)
    {
        if (lines.SequenceEqual(_visible)) return;
        _visible = lines;
        Changed?.Invoke();
    }
}
