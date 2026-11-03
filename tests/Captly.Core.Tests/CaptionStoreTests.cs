using Captly.Core.Captions;
using Captly.Core.Speech;

namespace Captly.Core.Tests;

public class CaptionStoreTests
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly FakeClock _clock = new();
    private readonly CaptionStore _store;

    public CaptionStoreTests() => _store = new CaptionStore(new CaptionOptions { CharsPerLine = 20 }, _clock);

    private void Partial(string text) => _store.Apply(new CaptionEvent(text, false, TimeSpan.Zero));
    private void Final(string text) => _store.Apply(new CaptionEvent(text, true, TimeSpan.Zero));

    private void Wait(double seconds)
    {
        _clock.Now += TimeSpan.FromSeconds(seconds);
        _store.Tick();
    }

    [Fact]
    public void Starts_Empty()
    {
        Assert.False(_store.HasText);
        Assert.Empty(_store.Lines);
    }

    [Fact]
    public void Partial_EditsTheBottomLineInPlace()
    {
        Partial("It was");
        Assert.Equal(["It was"], _store.Lines);

        Partial("It was the best");
        Assert.Equal(["It was the best"], _store.Lines);

        Partial("It is the best");
        Assert.Equal(["It is the best"], _store.Lines);
    }

    [Fact]
    public void LongPartial_ShowsItsNewestLines()
    {
        Partial("one two three four five six seven eight nine ten eleven twelve thirteen");

        Assert.Equal(2, _store.Lines.Count);
        Assert.EndsWith("thirteen", _store.Lines[1]);
    }

    [Fact]
    public void Final_ReplacesThePartial()
    {
        Partial("It was the bst");

        Final("It was the best.");

        Assert.Equal(["It was the best."], _store.Lines);
    }

    [Fact]
    public void NextPartial_GoesBelowTheFinal()
    {
        Final("First line.");

        Partial("Second");

        Assert.Equal(["First line.", "Second"], _store.Lines);
    }

    [Fact]
    public void Text_IsCleanedBeforeShowing()
    {
        Partial("  it  was. it is ");

        Assert.Equal(["It was. It is"], _store.Lines);
    }

    [Fact]
    public void NeverShowsMoreThanMaxLines()
    {
        var store = new CaptionStore(new CaptionOptions { MaxLines = 1, CharsPerLine = 20 }, _clock);

        store.Apply(new CaptionEvent("one two three four five six seven eight", false, default));

        Assert.Single(store.Lines);
    }

    [Fact]
    public void Final_HoldsItsPlaceForTheMinimumTime()
    {
        Final("Line one here");
        Final("Line two here");

        Partial("Line three here");
        Assert.Equal(["Line one here", "Line two here"], _store.Lines);

        Wait(1.1);
        Assert.Equal(["Line one here", "Line two here"], _store.Lines);

        Wait(0.2);
        Assert.Equal(["Line two here", "Line three here"], _store.Lines);
    }

    [Fact]
    public void HeldLine_GetsItsFullTimeOnceItAppears()
    {
        Final("Line one here");
        Final("Line two here");
        Final("Line three here");
        Assert.Equal(["Line one here", "Line two here"], _store.Lines);

        Wait(1.3);
        Assert.Equal(["Line two here", "Line three here"], _store.Lines);

        Final("Line four here");
        Wait(0.5);
        Assert.Contains("Line three here", _store.Lines);
    }

    [Fact]
    public void Backlog_NeverGrowsWithoutLimit()
    {
        const int sentences = 200;
        for (var i = 0; i < sentences; i++)
        {
            Final($"Sentence number {i}");
            _clock.Now += TimeSpan.FromSeconds(0.2);
            _store.Tick();
        }

        // Two screens of two lines may wait, however long the fast speech goes on.
        var newestShown = int.Parse(_store.Lines[^1].Split(' ')[^1]);
        Assert.InRange(sentences - 1 - newestShown, 0, 4);
    }

    [Fact]
    public void LongFinal_DoesNotJumpBackToItsStart()
    {
        Partial("one two three four five six seven eight nine ten eleven twelve");
        var shown = _store.Lines.ToList();

        Final("one two three four five six seven eight nine ten eleven twelve.");

        Assert.Equal(shown[1] + ".", _store.Lines[1]);
    }

    [Fact]
    public void EmptyFinal_ClearsThePartialWithoutCommitting()
    {
        Partial("Thank you for");

        Final("");

        Assert.False(_store.HasText);
    }

    [Fact]
    public void Silence_ClearsAfterTheSetting()
    {
        Final("Hello there.");

        Wait(3.9);
        Assert.True(_store.HasText);

        Wait(0.2);
        Assert.False(_store.HasText);
    }

    [Fact]
    public void NewText_RestartsTheSilenceTimer()
    {
        Final("Hello there.");
        Wait(3);
        Partial("And");

        Wait(3);
        Assert.True(_store.HasText);
    }

    [Fact]
    public void StuckPartial_StillClears()
    {
        Partial("Half a sent");

        Wait(5);

        Assert.False(_store.HasText);
    }

    [Fact]
    public void AfterClearing_NewTextShowsAtOnce()
    {
        Final("Hello there.");
        Wait(5);

        Partial("Back again");

        Assert.Equal(["Back again"], _store.Lines);
    }

    [Fact]
    public void Clear_EmptiesEverythingAtOnce()
    {
        Final("Before the seek.");
        Partial("Half");

        _store.Clear();

        Assert.False(_store.HasText);
        Partial("After");
        Assert.Equal(["After"], _store.Lines);
    }

    [Fact]
    public void Changed_FiresOnlyWhenTheLinesChange()
    {
        var count = 0;
        _store.Changed += () => count++;

        Partial("Hello");
        Partial("Hello");
        Wait(0.1);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Options_ComeFromAppearanceSettings()
    {
        var options = CaptionOptions.From(new Core.Settings.AppearanceSettings { MaxLines = 3, CharsPerLine = 30, ClearAfterMs = 8000 });

        Assert.Equal(new CaptionOptions { MaxLines = 3, CharsPerLine = 30, ClearAfter = TimeSpan.FromSeconds(8) }, options);
    }
}
