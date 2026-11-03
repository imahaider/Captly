using Captly.Core.Captions;

namespace Captly.Core.Tests;

public class CaptionTextTests
{
    [Theory]
    [InlineData("  hello   there  ", "Hello there")]
    [InlineData("one\ttwo\nthree", "One two three")]
    public void Normalize_CollapsesWhitespace(string input, string expected) =>
        Assert.Equal(expected, CaptionText.Normalize(input));

    [Theory]
    [InlineData("it was the best of times. it was the worst.", "It was the best of times. It was the worst.")]
    [InlineData("really? yes! good.", "Really? Yes! Good.")]
    [InlineData("the year 3.5 was fine", "The year 3.5 was fine")]
    public void Normalize_CapitalisesSentenceStarts(string input, string expected) =>
        Assert.Equal(expected, CaptionText.Normalize(input));

    [Fact]
    public void Normalize_FixesDoubledCommasFromTheModel() =>
        Assert.Equal("In short, the period", CaptionText.Normalize("In short,, the period"));

    [Fact]
    public void Normalize_LeavesMidSentenceCapitalsAlone() =>
        Assert.Equal("We went to Paris and NASA", CaptionText.Normalize("We went to Paris and NASA"));

    [Fact]
    public void Wrap_ShortTextStaysOnOneLine() => Assert.Equal(["Hello there"], CaptionText.Wrap("Hello there", 42));

    [Fact]
    public void Wrap_BreaksOnlyBetweenWords() =>
        Assert.Equal(["The quick brown", "fox jumps over", "the lazy dog"], CaptionText.Wrap("The quick brown fox jumps over the lazy dog", 15));

    [Fact]
    public void Wrap_FillsALineExactly() => Assert.Equal(["12345 789", "abc"], CaptionText.Wrap("12345 789 abc", 9));

    [Fact]
    public void Wrap_KeepsAnOverlongWordWhole() =>
        Assert.Equal(["a", "supercalifragilistic", "b"], CaptionText.Wrap("a supercalifragilistic b", 10));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Wrap_NothingGivesNoLines(string text) => Assert.Empty(CaptionText.Wrap(text, 42));
}
