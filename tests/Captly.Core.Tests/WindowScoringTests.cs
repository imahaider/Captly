using Captly.Core.Windowing;

namespace Captly.Core.Tests;

public class WindowScoringTests
{
    private const string Title = "Inside a jet engine";

    private static WindowInfo Window(nint handle, string title, int width = 1280, int height = 720) =>
        new(handle, 100, title, PixelRect.FromSize(0, 0, width, height));

    [Fact]
    public void TitleMatch_BeatsALargerForegroundWindow()
    {
        var video = Window(1, $"(3) {Title} - YouTube - Google Chrome", 800, 600);
        var other = Window(2, "Inbox - Google Chrome", 2560, 1440) with { IsForeground = true };

        Assert.Equal(1, WindowScoring.Pick([other, video], Title)!.Handle);
    }

    [Fact]
    public void WithoutATitleMatch_ForegroundWins()
    {
        var front = Window(1, "Notes", 800, 600) with { IsForeground = true };
        var back = Window(2, "Other", 1920, 1080);

        Assert.Equal(1, WindowScoring.Pick([back, front], Title)!.Handle);
    }

    [Fact]
    public void WithNothingElse_TheLargerWindowWins()
    {
        Assert.Equal(2, WindowScoring.Pick([Window(1, "Small", 640, 360), Window(2, "Large", 1600, 900)], null)!.Handle);
    }

    [Fact]
    public void AMinimisedVideoWindow_StillBeatsAnotherWindow_SoCaptionsHide()
    {
        var video = Window(1, Title) with { IsMinimised = true };
        var other = Window(2, "Inbox") with { IsForeground = true };

        Assert.Equal(1, WindowScoring.Pick([other, video], Title)!.Handle);
    }

    [Fact]
    public void AMinimisedWindow_LosesToAShownOne()
    {
        var hidden = Window(1, "A") with { IsMinimised = true };

        Assert.Equal(2, WindowScoring.Pick([hidden, Window(2, "B", 640, 360)], null)!.Handle);
    }

    [Fact]
    public void TheCurrentWindow_IsKeptOnANearTie()
    {
        Assert.Equal(2, WindowScoring.Pick([Window(1, "A", 1920, 1080), Window(2, "B", 1600, 900)], null, current: 2)!.Handle);
    }

    [Fact]
    public void Ties_GoToTheTopOfTheZOrder()
    {
        Assert.Equal(1, WindowScoring.Pick([Window(1, "A"), Window(2, "B")], null)!.Handle);
    }

    [Theory]
    [InlineData(false, false, false, false, 800, 600, true)]
    [InlineData(true, false, false, false, 800, 600, false)]
    [InlineData(false, true, false, false, 800, 600, false)]
    [InlineData(false, false, true, false, 800, 600, false)]
    [InlineData(false, false, false, true, 800, 600, false)]
    [InlineData(false, false, false, false, 100, 600, false)]
    [InlineData(false, false, false, false, 800, 50, false)]
    public void Candidates_AreShownMainWindowsOfAUsefulSize(bool hidden, bool cloaked, bool tool, bool owned, int width, int height, bool expected)
    {
        var window = Window(1, "A", width, height) with { IsVisible = !hidden, IsCloaked = cloaked, IsToolWindow = tool, HasOwner = owned };
        Assert.Equal(expected, WindowScoring.IsCandidate(window));
    }

    [Fact]
    public void AMinimisedWindow_IsACandidate_WhateverItsSize()
    {
        Assert.True(WindowScoring.IsCandidate(Window(1, "A", 0, 0) with { IsMinimised = true }));
    }

    [Fact]
    public void NoCandidates_PicksNothing()
    {
        Assert.Null(WindowScoring.Pick([Window(1, "A") with { IsToolWindow = true }], null));
    }

    [Theory]
    [InlineData("Inside a JET engine - YouTube", "inside a jet engine", true)]
    [InlineData("Inside a jet engine", "  Inside a jet engine  ", true)]
    [InlineData("YouTube", "Ab", false)]
    [InlineData("YouTube", "", false)]
    [InlineData("YouTube", null, false)]
    [InlineData("Something else", "Inside a jet engine", false)]
    public void TitleMatches_IgnoresCaseAndShortTitles(string title, string? media, bool expected) =>
        Assert.Equal(expected, WindowScoring.TitleMatches(title, media));
}
