using Captly.Core.Speech;

namespace Captly.Speech;

/// <summary>One line of an engine transcript, at the time of the update.</summary>
internal readonly record struct LineSnapshot(string Text, TimeSpan Start, bool IsComplete, TimeSpan Duration = default);

/// <summary>
/// Turns repeated transcript snapshots into caption events. The transcript only grows, complete
/// lines never change, and line indexes are stable, so it is enough to remember where the
/// unfinished part begins.
/// </summary>
internal sealed class LineTracker
{
    private readonly CaptionFilter _filter = new();
    private readonly List<CaptionEvent> _events = [];
    private int _firstOpenLine;
    private string _lastPartial = "";

    public void Reset()
    {
        _firstOpenLine = 0;
        _lastPartial = "";
        _filter.Reset();
    }

    /// <summary>
    /// Returns the events this update produced. The list is reused by the next call. A hidden final
    /// is still reported, with empty text, when a partial was showing, so that partial gets cleared.
    /// </summary>
    public IReadOnlyList<CaptionEvent> Update(ReadOnlySpan<LineSnapshot> lines)
    {
        _events.Clear();
        for (var i = _firstOpenLine; i < lines.Length; i++)
        {
            var line = lines[i];
            var text = line.Text.Trim();
            if (line.IsComplete)
            {
                var keep = _filter.AcceptFinal(text);
                if (keep) _events.Add(new CaptionEvent(text, true, line.Start) { Duration = line.Duration });
                else if (_lastPartial.Length > 0) _events.Add(new CaptionEvent("", true, line.Start) { Duration = line.Duration });
                _lastPartial = "";
                _firstOpenLine = i + 1;
            }
            else if (text != _lastPartial && CaptionFilter.HasWords(text))
            {
                _lastPartial = text;
                _events.Add(new CaptionEvent(text, false, line.Start) { Duration = line.Duration });
            }
        }
        return _events;
    }
}
