using System.Globalization;
using System.Text;
using Captly.Core.Captions;
using Captly.Core.Speech;

namespace Captly.Core.Transcripts;

public readonly record struct Cue(TimeSpan Start, TimeSpan End, IReadOnlyList<string> Lines);

/// <summary>Formatting for transcripts. Pure, so it is easy to test.</summary>
public static class TranscriptFormat
{
    private static readonly TimeSpan MinimumCue = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Where a finished line sits on the media's timeline. The engine says how far back the line
    /// started from what it had heard, and that gap is measured back from the media position now.
    /// </summary>
    public static (TimeSpan Start, TimeSpan End) Place(CaptionEvent caption, TimeSpan mediaNow)
    {
        var start = mediaNow - (caption.HeardUpTo - caption.StreamTime);
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;
        var length = caption.Duration > MinimumCue ? caption.Duration : MinimumCue;
        return (start, start + length);
    }

    /// <summary>Wraps a line for SRT, and splits it into cues of at most two lines, timed by length.</summary>
    public static List<Cue> SplitCue(string text, TimeSpan start, TimeSpan end, int charsPerLine = 42, int linesPerCue = 2)
    {
        var lines = CaptionText.Wrap(text, charsPerLine);
        var cues = new List<Cue>();
        if (lines.Count == 0) return cues;

        var total = (double)lines.Sum(l => l.Length);
        var span = end - start;
        var at = start;
        for (var i = 0; i < lines.Count; i += linesPerCue)
        {
            var chunk = lines.Skip(i).Take(linesPerCue).ToList();
            var until = i + linesPerCue >= lines.Count ? end : at + span * (chunk.Sum(l => l.Length) / total);
            cues.Add(new Cue(at, until, chunk));
            at = until;
        }
        return cues;
    }

    public static string Srt(int index, Cue cue)
    {
        var text = new StringBuilder();
        text.Append(index.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        text.Append(Timestamp(cue.Start)).Append(" --> ").Append(Timestamp(cue.End)).Append("\r\n");
        foreach (var line in cue.Lines) text.Append(line).Append("\r\n");
        return text.Append("\r\n").ToString();
    }

    /// <summary>SRT time: hours:minutes:seconds,milliseconds.</summary>
    public static string Timestamp(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
    }
}
