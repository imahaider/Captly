using Captly.Core.Speech;

namespace Captly.Speech.Tests;

public class LineTrackerTests
{
    private static LineSnapshot Open(string text, double start = 0) => new(text, TimeSpan.FromSeconds(start), false);
    private static LineSnapshot Done(string text, double start = 0) => new(text, TimeSpan.FromSeconds(start), true);

    private static CaptionEvent Single(LineTracker tracker, params LineSnapshot[] lines) =>
        Assert.Single(tracker.Update(lines));

    [Fact]
    public void GrowingLine_GivesPartials()
    {
        var tracker = new LineTracker();

        Assert.Equal(new CaptionEvent("It was", false, TimeSpan.Zero), Single(tracker, Open("It was")));
        Assert.Equal("It was the best", Single(tracker, Open("It was the best")).Text);
    }

    [Fact]
    public void UnchangedLine_GivesNothing()
    {
        var tracker = new LineTracker();
        tracker.Update([Open("Hello")]);

        Assert.Empty(tracker.Update([Open("Hello")]));
    }

    [Fact]
    public void CompletedLine_GivesOneFinal()
    {
        var tracker = new LineTracker();
        tracker.Update([Open("Hello")]);

        var final = Single(tracker, Done("Hello there.", 2));
        Assert.True(final.IsFinal);
        Assert.Equal("Hello there.", final.Text);
        Assert.Equal(TimeSpan.FromSeconds(2), final.StreamTime);

        Assert.Empty(tracker.Update([Done("Hello there.", 2)]));
    }

    [Fact]
    public void NextLine_FollowsAFinishedOne()
    {
        var tracker = new LineTracker();
        tracker.Update([Done("First."), Open("Second", 3)]);

        var next = Single(tracker, Done("First."), Open("Second line", 3));

        Assert.Equal("Second line", next.Text);
        Assert.Equal(TimeSpan.FromSeconds(3), next.StreamTime);
    }

    [Fact]
    public void SeveralLinesFinishingAtOnce_AreEachReported()
    {
        var events = new LineTracker().Update([Done("One."), Done("Two."), Open("Three")]);

        Assert.Equal(["One.", "Two.", "Three"], events.Select(e => e.Text));
        Assert.Equal([true, true, false], events.Select(e => e.IsFinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void EmptyOrPunctuationOnly_IsNeverShown(string text)
    {
        var tracker = new LineTracker();

        Assert.Empty(tracker.Update([Open(text)]));
        Assert.Empty(tracker.Update([Done(text)]));
    }

    [Fact]
    public void JunkLine_ClearsItsPartial()
    {
        var tracker = new LineTracker();
        tracker.Update([Open("Thank you for watching")]);

        var cleared = Single(tracker, Done("Thank you for watching."));

        Assert.True(cleared.IsFinal);
        Assert.Equal("", cleared.Text);
    }

    [Fact]
    public void JunkAtLineEnd_WithNoPartial_GivesNothing() =>
        Assert.Empty(new LineTracker().Update([Done("Thanks for watching!")]));

    [Fact]
    public void ExactRepeat_IsDropped()
    {
        var events = new LineTracker().Update([Done("Hello there."), Done("Hello there."), Done("Goodbye.")]);

        Assert.Equal(["Hello there.", "Goodbye."], events.Select(e => e.Text));
    }

    [Fact]
    public void Reset_StartsAgainFromLineZero()
    {
        var tracker = new LineTracker();
        tracker.Update([Done("Before the seek."), Open("Half a line")]);

        tracker.Reset();

        Assert.Equal("Before the seek.", Single(tracker, Done("Before the seek.")).Text);
        Assert.Equal("Half a line", Single(tracker, Done("Before the seek."), Open("Half a line")).Text);
    }

    [Fact]
    public void Text_IsTrimmed() => Assert.Equal("Hello", Single(new LineTracker(), Open("  Hello ")).Text);
}
