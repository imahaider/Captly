using Captly.Core.Settings;
using Captly.Core.Windowing;

namespace Captly.Core.Tests;

public class CaptionPlacementTests
{
    private static readonly MonitorInfo Screen = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.25);
    private static readonly PositionSettings Position = new();
    private static readonly AppearanceSettings Appearance = new();

    private static WindowPlacement Window(PixelRect bounds) => new(1, bounds, Screen);

    [Fact]
    public void OnAWindow_CentredAndUpByTheBottomOffset()
    {
        var layout = CaptionPlacement.OnWindow(Window(PixelRect.FromSize(100, 50, 1000, 800)), Position, Appearance);

        Assert.Equal(new PixelPoint(600, 850 - 96), layout.Anchor);
        Assert.Equal(PixelRect.FromSize(100, 50, 1000, 800), layout.Bounds);
        Assert.True(layout.Visible);
    }

    [Fact]
    public void MaximumWidth_IsAShareOfTheWindow_InDips()
    {
        var layout = CaptionPlacement.OnWindow(Window(PixelRect.FromSize(0, 0, 1000, 800)), Position with { MaxWidthPercent = 50 }, Appearance);

        Assert.Equal(1.25, layout.Scale);
        Assert.Equal(400, layout.MaxWidth, 6);
    }

    [Fact]
    public void TextSize_IsTheChosenSize_AtTheReferenceHeight()
    {
        var layout = CaptionPlacement.OnWindow(Window(PixelRect.FromSize(0, 0, 1200, 900)), Position, Appearance);

        Assert.Equal(22, layout.FontSize, 6);
    }

    [Theory]
    [InlineData(360, 18)]
    [InlineData(720, 22)]
    [InlineData(1080, 33)]
    [InlineData(2000, 40)]
    public void TextSize_ScalesWithWindowHeight_WithinTheLimits(double heightDips, double expected)
    {
        Assert.Equal(expected, CaptionPlacement.ScaledFontSize(heightDips, Appearance), 6);
    }

    [Fact]
    public void TextSize_ChosenOutsideTheLimits_IsStillReachable()
    {
        Assert.Equal(14, CaptionPlacement.ScaledFontSize(720, Appearance with { TextSize = 14 }), 6);
        Assert.Equal(44, CaptionPlacement.ScaledFontSize(720, Appearance with { TextSize = 44 }), 6);
    }

    [Fact]
    public void Fullscreen_IsAWindowCoveringItsWholeMonitor()
    {
        Assert.True(Window(Screen.Bounds).IsFullscreen);
        Assert.True(Window(new PixelRect(-8, -8, 1928, 1088)).IsFullscreen);
        Assert.False(Window(Screen.WorkArea).IsFullscreen);
        Assert.False((Window(Screen.Bounds) with { IsMinimised = true }).IsFullscreen);
    }

    [Fact]
    public void Fullscreen_ScreenBottom_SitsLowerThanInAWindow()
    {
        var layout = CaptionPlacement.OnWindow(Window(Screen.Bounds), Position, Appearance);

        Assert.Equal(new PixelPoint(960, 1080 - 54), layout.Anchor);
    }

    [Fact]
    public void Fullscreen_SameAsWindow_KeepsTheBottomOffset()
    {
        var layout = CaptionPlacement.OnWindow(Window(Screen.Bounds), Position with { Fullscreen = FullscreenPosition.SameAsWindow }, Appearance);

        Assert.Equal(new PixelPoint(960, 1080 - 130), layout.Anchor);
    }

    [Fact]
    public void Fullscreen_UsesTheMonitor_EvenIfTheWindowOverhangsIt()
    {
        var layout = CaptionPlacement.OnWindow(Window(new PixelRect(-8, -8, 1928, 1088)), Position, Appearance);

        Assert.Equal(Screen.Bounds, layout.Bounds);
    }

    [Fact]
    public void MinimisedOrHidden_HidesTheCaptions()
    {
        var bounds = PixelRect.FromSize(0, 0, 1000, 800);
        Assert.False(CaptionPlacement.OnWindow(Window(bounds) with { IsMinimised = true }, Position, Appearance).Visible);
        Assert.False(CaptionPlacement.OnWindow(Window(bounds) with { IsHidden = true }, Position, Appearance).Visible);
    }

    [Fact]
    public void AWindowOverTheCaptions_HidesThem()
    {
        var bounds = PixelRect.FromSize(0, 0, 1000, 800);
        var over = PixelRect.FromSize(300, 500, 400, 300);

        Assert.False(CaptionPlacement.OnWindow(Window(bounds) with { Above = [over] }, Position, Appearance).Visible);
    }

    [Fact]
    public void AWindowElsewhere_LeavesThemShown()
    {
        var bounds = PixelRect.FromSize(0, 0, 1000, 800);
        var corner = PixelRect.FromSize(0, 0, 300, 300);
        var edge = PixelRect.FromSize(0, 600, 150, 200);

        Assert.True(CaptionPlacement.OnWindow(Window(bounds) with { Above = [corner, edge] }, Position, Appearance).Visible);
    }

    [Fact]
    public void Band_CoversTheMaximumWidth_AndTheAllowedLines()
    {
        var layout = new CaptionLayout(new PixelPoint(500, 700), PixelRect.FromSize(0, 0, 1000, 800), 1, 20, 800);

        var band = CaptionPlacement.Band(layout, 2);

        Assert.Equal(new PixelRect(100, 700 - 70, 900, 700), band);
    }

    [Fact]
    public void OnScreen_UsesTheWorkArea_AndTheChosenSize()
    {
        var layout = CaptionPlacement.OnScreen(Screen, Position, Appearance with { TextSize = 26 });

        Assert.Equal(new PixelPoint(960, 1032 - 124), layout.Anchor);
        Assert.Equal(26, layout.FontSize);
        Assert.Equal(1920 / 1.25 * 0.8, layout.MaxWidth, 6);
    }

    [Fact]
    public void AtPoint_KeepsTheDraggedSpot()
    {
        var layout = CaptionPlacement.AtPoint(new PixelPoint(300, 400), Screen, Position, Appearance);

        Assert.Equal(new PixelPoint(300, 400), layout.Anchor);
        Assert.Equal(Screen.WorkArea, layout.Bounds);
    }

    [Fact]
    public void Fit_PutsTheBottomCentreOnTheAnchor()
    {
        var layout = new CaptionLayout(new PixelPoint(500, 700), PixelRect.FromSize(0, 0, 1000, 800), 1, 22, 800);

        Assert.Equal(new PixelPoint(400, 640), CaptionPlacement.Fit(layout, 200, 60));
    }

    [Fact]
    public void Fit_KeepsTheBubbleInsideTheBounds()
    {
        var layout = new CaptionLayout(new PixelPoint(20, 30), PixelRect.FromSize(0, 0, 1000, 800), 1, 22, 800);

        Assert.Equal(new PixelPoint(0, 0), CaptionPlacement.Fit(layout, 200, 60));
        Assert.Equal(new PixelPoint(800, 640), CaptionPlacement.Fit(layout with { Anchor = new PixelPoint(990, 700) }, 200, 60));
    }

    [Fact]
    public void Fit_CentresABubbleWiderThanTheBounds()
    {
        var layout = new CaptionLayout(new PixelPoint(900, 700), PixelRect.FromSize(0, 0, 1000, 800), 1, 22, 800);

        Assert.Equal(-100, CaptionPlacement.Fit(layout, 1200, 60).X);
    }

    [Fact]
    public void SameAs_ComparesTheWindowsAbove()
    {
        var a = Window(Screen.WorkArea) with { Above = [PixelRect.FromSize(0, 0, 10, 10)] };

        Assert.True(a.SameAs(a with { Above = [PixelRect.FromSize(0, 0, 10, 10)] }));
        Assert.False(a.SameAs(a with { Above = [] }));
        Assert.False(a.SameAs(a with { IsMinimised = true }));
        Assert.False(a.SameAs(null));
    }

    [Fact]
    public void PixelRect_IntersectsAndContains()
    {
        var a = new PixelRect(0, 0, 100, 100);

        Assert.Equal(new PixelRect(50, 50, 100, 100), a.Intersect(new PixelRect(50, 50, 200, 200)));
        Assert.True(a.Intersect(new PixelRect(100, 0, 200, 100)).IsEmpty);
        Assert.True(a.Contains(new PixelPoint(99, 0)));
        Assert.False(a.Contains(new PixelPoint(100, 0)));
        Assert.Equal(new PixelPoint(50, 50), a.Center);
    }
}
