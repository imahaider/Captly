using System.Globalization;
using System.Text;
using Captly.Core.Captions;
using Captly.Core.Settings;
using Captly.Core.Speech;

namespace Captly.Core.Transcripts;

/// <summary>
/// Writes one transcript per video as TXT and SRT. Files are only created when the first line
/// arrives, so a video with no speech leaves nothing behind, and every line is flushed at once,
/// so a crash loses nothing.
/// </summary>
/// <remarks>The TXT file starts with the title, then "app · date", then a blank line. <see cref="TranscriptIndex"/> reads them back.</remarks>
public sealed class TranscriptWriter(TranscriptSettings settings, string folder, TimeProvider? clock = null) : IDisposable
{
    internal const string Separator = " · ";

    // A byte order mark tells older Windows tools and players that the text is UTF-8.
    private static readonly Encoding Utf8WithMark = new UTF8Encoding(true);

    // Cues this close together are the same stretch of speech, so they are kept from overlapping.
    private static readonly TimeSpan OverlapWindow = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string? _title;
    private string _app = "Captly";
    private DateTimeOffset _begunAt = (clock ?? TimeProvider.System).GetLocalNow();
    private StreamWriter? _txt;
    private StreamWriter? _srt;
    private int _cue;
    private TimeSpan _lastEnd;
    private bool _pendingBreak;

    /// <summary>When the current transcript began, for timing lines when there is no media position.</summary>
    public DateTimeOffset BegunAt => _begunAt;

    /// <summary>The files of the current transcript, once it has any.</summary>
    public IReadOnlyList<string> CurrentFiles { get; private set; } = [];

    private bool Enabled => settings.Enabled && (settings.Txt || settings.Srt);

    /// <summary>Ends the current transcript and starts another for this video and app.</summary>
    public void Begin(string? title, string app)
    {
        End();
        _title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        _app = app;
        _begunAt = _clock.GetLocalNow();
        _cue = 0;
        _lastEnd = TimeSpan.Zero;
    }

    /// <summary>Adds a finished line, with its place on the media's timeline.</summary>
    public void Add(string text, TimeSpan start, TimeSpan end)
    {
        if (!Enabled) return;
        text = CaptionText.Normalize(text);
        if (!CaptionFilter.HasWords(text)) return;
        Open();

        if (_txt != null)
        {
            if (_pendingBreak) _txt.WriteLine();
            _txt.WriteLine(text);
            _txt.Flush();
        }
        _pendingBreak = false;

        if (_srt != null)
        {
            if (start < _lastEnd && start > _lastEnd - OverlapWindow)
            {
                end += _lastEnd - start;
                start = _lastEnd;
            }
            _lastEnd = end;
            foreach (var cue in TranscriptFormat.SplitCue(text, start, end)) _srt.Write(TranscriptFormat.Srt(++_cue, cue));
            _srt.Flush();
        }
    }

    /// <summary>Marks a jump in the video, as a blank line in the text.</summary>
    public void Break() => _pendingBreak = _txt != null;

    public void End()
    {
        _txt?.Dispose();
        _srt?.Dispose();
        _txt = null;
        _srt = null;
        _pendingBreak = false;
        CurrentFiles = [];
    }

    public void Dispose() => End();

    private void Open()
    {
        if (_txt != null || _srt != null) return;

        Directory.CreateDirectory(folder);
        var stem = TranscriptNames.Unique(folder, TranscriptNames.Stem(_title, _app, _begunAt, settings.NameAfterVideo));
        var files = new List<string>();
        if (settings.Txt)
        {
            var path = Path.Combine(folder, stem + ".txt");
            _txt = Create(path, Utf8WithMark);
            _txt.WriteLine(_title ?? _app);
            _txt.WriteLine(string.Create(CultureInfo.CurrentCulture, $"{_app}{Separator}{_begunAt:d MMM yyyy, HH:mm}"));
            _txt.WriteLine();
            files.Add(path);
        }
        if (settings.Srt)
        {
            var path = Path.Combine(folder, stem + ".srt");
            _srt = Create(path, Utf8WithMark);
            files.Add(path);
        }
        CurrentFiles = files;
    }

    // Shared, so the user can open a transcript while it is still being written.
    private static StreamWriter Create(string path, Encoding encoding) =>
        new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), encoding);
}

public sealed record TranscriptEntry(string Title, string? Source, DateTime LastWritten, string? TxtPath, string? SrtPath);

/// <summary>Reads the transcript folder for the Recent list.</summary>
public static class TranscriptIndex
{
    public static List<TranscriptEntry> Recent(string folder, int count = 5)
    {
        if (!Directory.Exists(folder)) return [];

        return Directory.EnumerateFiles(folder)
            .Where(f => TranscriptNames.Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var txt = g.FirstOrDefault(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
                var srt = g.FirstOrDefault(f => f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));
                var (title, source) = ReadHeader(txt);
                return new TranscriptEntry(title ?? TitleFromStem(g.Key), source, g.Max(File.GetLastWriteTime), txt, srt);
            })
            .OrderByDescending(e => e.LastWritten)
            .Take(count)
            .ToList();
    }

    private static (string? Title, string? Source) ReadHeader(string? txt)
    {
        if (txt == null) return (null, null);
        try
        {
            using var reader = new StreamReader(new FileStream(txt, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            var title = reader.ReadLine();
            var details = reader.ReadLine();
            var source = details?.Contains(TranscriptWriter.Separator) == true ? details[..details.IndexOf(TranscriptWriter.Separator, StringComparison.Ordinal)] : null;
            return (string.IsNullOrWhiteSpace(title) ? null : title, source);
        }
        catch (IOException)
        {
            return (null, null);
        }
    }

    private static string TitleFromStem(string stem)
    {
        var dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        return dash >= 0 ? stem[(dash + 3)..] : stem;
    }
}
