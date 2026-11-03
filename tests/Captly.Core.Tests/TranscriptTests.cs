using System.Text;
using Captly.Core.Settings;
using Captly.Core.Speech;
using Captly.Core.Transcripts;

namespace Captly.Core.Tests;

public class TranscriptFormatTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Theory]
    [InlineData(0, "00:00:00,000")]
    [InlineData(61.5, "00:01:01,500")]
    [InlineData(3723.042, "01:02:03,042")]
    [InlineData(-3, "00:00:00,000")]
    public void Timestamp_UsesSrtFormat(double seconds, string expected) => Assert.Equal(expected, TranscriptFormat.Timestamp(S(seconds)));

    [Fact]
    public void Srt_WritesACue() =>
        Assert.Equal("3\r\n00:00:01,000 --> 00:00:02,500\r\nHello there.\r\n\r\n",
            TranscriptFormat.Srt(3, new Cue(S(1), S(2.5), ["Hello there."])));

    [Fact]
    public void Place_MeasuresBackFromTheMediaPosition()
    {
        // The line began 4 s into the stream, and the engine had heard 6.5 s when it finished.
        var caption = new CaptionEvent("Hello", true, S(4)) { Duration = S(2), HeardUpTo = S(6.5) };

        var (start, end) = TranscriptFormat.Place(caption, mediaNow: S(100));

        Assert.Equal(S(97.5), start);
        Assert.Equal(S(99.5), end);
    }

    [Fact]
    public void Place_NeverStartsBeforeZero_AndLastsAtLeastHalfASecond()
    {
        var (start, end) = TranscriptFormat.Place(new CaptionEvent("Hi", true, S(0)) { HeardUpTo = S(5) }, S(2));

        Assert.Equal(TimeSpan.Zero, start);
        Assert.Equal(S(0.5), end);
    }

    [Fact]
    public void ShortLine_IsOneCue()
    {
        var cue = Assert.Single(TranscriptFormat.SplitCue("Hello there.", S(1), S(2)));
        Assert.Equal(new[] { "Hello there." }, cue.Lines);
        Assert.Equal(S(1), cue.Start);
        Assert.Equal(S(2), cue.End);
    }

    [Fact]
    public void LongLine_SplitsIntoTwoLineCues_TimedByLength()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 40));

        var cues = TranscriptFormat.SplitCue(text, S(0), S(10));

        Assert.True(cues.Count > 1);
        Assert.All(cues, c => Assert.InRange(c.Lines.Count, 1, 2));
        Assert.All(cues, c => Assert.All(c.Lines, l => Assert.True(l.Length <= 42)));
        Assert.Equal(S(0), cues[0].Start);
        Assert.Equal(S(10), cues[^1].End);
        for (var i = 1; i < cues.Count; i++) Assert.Equal(cues[i - 1].End, cues[i].Start);
    }
}

public sealed class TranscriptNamesTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 3, 14, 5, 0, TimeSpan.Zero);
    private readonly string _folder = Directory.CreateTempSubdirectory("captly-names-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void NamedAfterTheVideo() => Assert.Equal("2026-11-03 - Inside a jet engine", TranscriptNames.Stem("Inside a jet engine", "Google Chrome", At, true));

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("Inside a jet engine", false)]
    public void WithoutATitle_OrWhenTurnedOff_NamedAfterTheApp(string? title, bool nameAfterVideo) =>
        Assert.Equal("2026-11-03 14-05 - Google Chrome", TranscriptNames.Stem(title, "Google Chrome", At, nameAfterVideo));

    [Theory]
    [InlineData("What? A <test>: \"yes\" / no | maybe*", "What A test yes no maybe")]
    [InlineData("Line\none\ttwo", "Line one two")]
    [InlineData("Ends with dots...", "Ends with dots")]
    [InlineData("???", "Untitled")]
    [InlineData("CON", "_CON")]
    public void Clean_MakesSafeFileNames(string input, string expected) => Assert.Equal(expected, TranscriptNames.Clean(input));

    [Fact]
    public void Clean_LimitsLength() => Assert.Equal(100, TranscriptNames.Clean(new string('a', 300)).Length);

    [Fact]
    public void Unique_NumbersRepeatedNames()
    {
        Assert.Equal("Talk", TranscriptNames.Unique(_folder, "Talk"));

        File.WriteAllText(Path.Combine(_folder, "Talk.srt"), "");
        Assert.Equal("Talk (2)", TranscriptNames.Unique(_folder, "Talk"));

        File.WriteAllText(Path.Combine(_folder, "Talk (2).txt"), "");
        Assert.Equal("Talk (3)", TranscriptNames.Unique(_folder, "Talk"));
    }

    [Fact]
    public void DefaultFolder_IsTheRealDocumentsFolder() =>
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Captly"),
            TranscriptNames.ResolveFolder(new TranscriptSettings().Folder));

    [Fact]
    public void OtherFolders_ExpandVariables() =>
        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("TEMP")!, "x"), TranscriptNames.ResolveFolder(@"%TEMP%\x"));
}

public sealed class TranscriptWriterTests : IDisposable
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 14, 5, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly string _folder = Path.Combine(Directory.CreateTempSubdirectory("captly-transcripts-").FullName, "Captly");
    private readonly FakeClock _clock = new();

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_folder)!, recursive: true);

    private TranscriptWriter Writer(TranscriptSettings? settings = null) => new(settings ?? new TranscriptSettings(), _folder, _clock);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    // Reads the way another app would, while Captly may still be writing.
    private string Read(string name)
    {
        using var stream = new FileStream(Path.Combine(_folder, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(stream, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public void WritesTxtAndSrt_NamedAfterTheVideo()
    {
        using var writer = Writer();
        writer.Begin("Inside a jet engine", "Google Chrome");

        writer.Add("the fan pulls in air.", S(1), S(3));
        writer.Add("Most of it flows around the core.", S(3.5), S(6));

        Assert.Equal("Inside a jet engine\nGoogle Chrome · 3 Nov 2026, 14:05\n\nThe fan pulls in air.\nMost of it flows around the core.\n",
            Read("2026-11-03 - Inside a jet engine.txt").ReplaceLineEndings("\n"));
        Assert.Equal("1\r\n00:00:01,000 --> 00:00:03,000\r\nThe fan pulls in air.\r\n\r\n2\r\n00:00:03,500 --> 00:00:06,000\r\nMost of it flows around the core.\r\n\r\n",
            Read("2026-11-03 - Inside a jet engine.srt"));
    }

    [Fact]
    public void Cues_DoNotOverlap_ButASeekBackIsKept()
    {
        using var writer = Writer(new TranscriptSettings { Txt = false });
        writer.Begin("Video", "Chrome");
        writer.Add("One.", S(1), S(5.1));
        writer.Add("Two.", S(5), S(7));
        writer.Add("Three.", S(2), S(3));

        var srt = Read("2026-11-03 - Video.srt");
        Assert.Contains("00:00:05,100 --> 00:00:07,100", srt);
        Assert.Contains("00:00:02,000 --> 00:00:03,000", srt);
    }

    [Fact]
    public void Files_StartWithAUtf8Mark()
    {
        using var writer = Writer();
        writer.Begin("Video", "Chrome");
        writer.Add("Hello.", S(1), S(2));
        writer.End();

        Assert.All(Directory.GetFiles(_folder), f => Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(f)[..3]));
    }

    [Fact]
    public void NothingSaid_LeavesNoFiles()
    {
        using var writer = Writer();
        writer.Begin("Silent film", "Chrome");
        writer.Add("...", S(1), S(2));
        writer.End();

        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Fact]
    public void Disabled_WritesNothing()
    {
        using var writer = Writer(new TranscriptSettings { Enabled = false });
        writer.Begin("Video", "Chrome");
        writer.Add("Hello there.", S(1), S(2));

        Assert.False(Directory.Exists(_folder));
    }

    [Theory]
    [InlineData(true, false, ".txt")]
    [InlineData(false, true, ".srt")]
    public void OnlyTheChosenTypes_AreWritten(bool txt, bool srt, string expected)
    {
        using var writer = Writer(new TranscriptSettings { Txt = txt, Srt = srt });
        writer.Begin("Video", "Chrome");
        writer.Add("Hello there.", S(1), S(2));

        Assert.Equal([expected], Directory.GetFiles(_folder).Select(Path.GetExtension));
    }

    [Fact]
    public void NewVideo_StartsANewFile_AndClosesTheOld()
    {
        using var writer = Writer();
        writer.Begin("First", "Chrome");
        writer.Add("One.", S(1), S(2));
        var first = writer.CurrentFiles.ToList();

        writer.Begin("Second", "Chrome");
        writer.Add("Two.", S(1), S(2));

        Assert.Equal(4, Directory.GetFiles(_folder).Length);
        Assert.All(first, f => File.Move(f, f + ".moved"));
    }

    [Fact]
    public void Index_ReadsATranscriptStillBeingWritten()
    {
        using var writer = Writer();
        writer.Begin("Live", "Google Chrome");
        writer.Add("Hello.", S(1), S(2));

        var entry = Assert.Single(TranscriptIndex.Recent(_folder));
        Assert.Equal("Live", entry.Title);
        Assert.Equal("Google Chrome", entry.Source);
    }

    [Fact]
    public void SameVideoAgain_GetsANumberedFile()
    {
        using var writer = Writer();
        writer.Begin("Talk", "Chrome");
        writer.Add("One.", S(1), S(2));
        writer.Begin("Talk", "Chrome");
        writer.Add("Two.", S(1), S(2));

        Assert.True(File.Exists(Path.Combine(_folder, "2026-11-03 - Talk (2).txt")));
    }

    [Fact]
    public void Seek_LeavesABlankLineInTheText()
    {
        using var writer = Writer(new TranscriptSettings { Srt = false });
        writer.Begin("Video", "Chrome");
        writer.Add("Before.", S(1), S(2));
        writer.Break();
        writer.Add("After.", S(300), S(301));

        Assert.EndsWith("Before.\n\nAfter.\n", Read("2026-11-03 - Video.txt").ReplaceLineEndings("\n"));
    }

    [Fact]
    public void NoTitle_NamesTheFileAfterTheApp()
    {
        using var writer = Writer();
        writer.Begin(null, "Google Chrome");
        writer.Add("Hello.", S(1), S(2));

        Assert.True(File.Exists(Path.Combine(_folder, "2026-11-03 14-05 - Google Chrome.txt")));
    }

    [Fact]
    public void Index_ListsTheNewestFirst_WithTitleSourceAndTypes()
    {
        using (var writer = Writer())
        {
            writer.Begin("Older: video?", "VLC media player");
            writer.Add("One.", S(1), S(2));
            writer.Begin("Inside a jet engine", "Google Chrome");
            writer.Add("Two.", S(1), S(2));
        }
        File.SetLastWriteTime(Path.Combine(_folder, "2026-11-03 - Older video.txt"), DateTime.Now.AddHours(-2));
        File.SetLastWriteTime(Path.Combine(_folder, "2026-11-03 - Older video.srt"), DateTime.Now.AddHours(-2));
        File.WriteAllText(Path.Combine(_folder, "notes.md"), "not a transcript");

        var recent = TranscriptIndex.Recent(_folder);

        Assert.Equal(["Inside a jet engine", "Older: video?"], recent.Select(e => e.Title));
        Assert.Equal("Google Chrome", recent[0].Source);
        Assert.NotNull(recent[0].TxtPath);
        Assert.NotNull(recent[0].SrtPath);
    }

    [Fact]
    public void Index_SrtOnly_TakesTheTitleFromTheFileName()
    {
        using (var writer = Writer(new TranscriptSettings { Txt = false }))
        {
            writer.Begin("Just subtitles", "Chrome");
            writer.Add("One.", S(1), S(2));
        }

        var entry = Assert.Single(TranscriptIndex.Recent(_folder));
        Assert.Equal("Just subtitles", entry.Title);
        Assert.Null(entry.Source);
    }

    [Fact]
    public void Index_MissingFolder_IsEmpty() => Assert.Empty(TranscriptIndex.Recent(Path.Combine(_folder, "nope")));
}
