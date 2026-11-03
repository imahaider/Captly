using Captly.Core.Speech;

namespace Captly.Core.Tests;

public class CaptionFilterTests
{
    [Theory]
    [InlineData("Hello there.", true)]
    [InlineData("3", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("...", false)]
    [InlineData("?!", false)]
    public void HasWords_NeedsALetterOrDigit(string text, bool expected) =>
        Assert.Equal(expected, CaptionFilter.HasWords(text));

    [Theory]
    [InlineData("Thank you for watching.")]
    [InlineData("THANKS FOR WATCHING!")]
    [InlineData("  please   subscribe ")]
    [InlineData("You.")]
    [InlineData("Bye...")]
    public void KnownJunk_IsDropped(string text) => Assert.False(new CaptionFilter().AcceptFinal(text));

    [Theory]
    [InlineData("Thank you for watching this video on birds.")]
    [InlineData("You are right.")]
    [InlineData("Goodbye, everyone.")]
    public void RealSpeechContainingJunkWords_IsKept(string text) => Assert.True(new CaptionFilter().AcceptFinal(text));

    [Fact]
    public void ExactRepeat_IsDroppedEvenWithOtherPunctuation()
    {
        var filter = new CaptionFilter();

        Assert.True(filter.AcceptFinal("Hello there."));
        Assert.False(filter.AcceptFinal("hello there!"));
    }

    [Fact]
    public void Repeat_IsAllowedAfterSomethingElse()
    {
        var filter = new CaptionFilter();

        filter.AcceptFinal("Yes.");
        filter.AcceptFinal("No.");

        Assert.True(filter.AcceptFinal("Yes."));
    }

    [Fact]
    public void DroppedJunk_DoesNotCountAsTheLastLine()
    {
        var filter = new CaptionFilter();

        filter.AcceptFinal("Yes.");
        filter.AcceptFinal("Please subscribe.");

        Assert.False(filter.AcceptFinal("Yes."));
    }

    [Fact]
    public void Reset_ForgetsTheLastLine()
    {
        var filter = new CaptionFilter();
        filter.AcceptFinal("Yes.");

        filter.Reset();

        Assert.True(filter.AcceptFinal("Yes."));
    }

    [Fact]
    public void JunkList_CanBeReplaced() => Assert.False(new CaptionFilter(["like and share"]).AcceptFinal("Like and share!"));
}
