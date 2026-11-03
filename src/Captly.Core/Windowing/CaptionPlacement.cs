using Captly.Core.Settings;

namespace Captly.Core.Windowing;

/// <summary>
/// Where the caption goes. The anchor is the bubble's bottom centre, in physical pixels, and the
/// bubble is kept inside the bounds. Font size and maximum width are in device-independent units
/// at the given scale, which is the DPI of the monitor the captions sit on.
/// </summary>
public sealed record CaptionLayout(PixelPoint Anchor, PixelRect Bounds, double Scale, double FontSize, double MaxWidth, bool Visible = true);

/// <summary>Works out the caption's place from the video window and the Position settings. Pure, so all of it is tested.</summary>
public static class CaptionPlacement
{
    /// <summary>The window height, in DIPs, at which captions use the chosen text size.</summary>
    public const double ReferenceHeight = 720;

    /// <summary>Distance from the bottom in fullscreen when "Bottom of the screen" is chosen. Players hide their controls there.</summary>
    public const int FullscreenBottomPercent = 5;

    // For the covered check: two lines of text, plus the bubble's padding.
    private const double LineHeight = 1.3;
    private const double BubblePadding = 18;
    private const int CoveredFraction = 3;

    /// <summary>
    /// On the video window, scaled with its height. Hidden while the window is minimised, hidden,
    /// or another window covers the spot where the captions go.
    /// </summary>
    public static CaptionLayout OnWindow(WindowPlacement window, PositionSettings position, AppearanceSettings appearance)
    {
        var fullscreen = window.IsFullscreen;
        var area = fullscreen ? window.Monitor.Bounds : window.Bounds;
        var bottom = fullscreen && position.Fullscreen == FullscreenPosition.ScreenBottom ? FullscreenBottomPercent : position.BottomOffsetPercent;
        var scale = window.Monitor.Scale;
        var layout = InArea(area, scale, bottom, position.MaxWidthPercent, ScaledFontSize(area.Height / scale, appearance));
        return layout with { Visible = !window.IsMinimised && !window.IsHidden && !IsCovered(layout, appearance.MaxLines, window.Above) };
    }

    /// <summary>At the bottom of a screen, at the chosen text size. Used when there is no window to pin to.</summary>
    public static CaptionLayout OnScreen(MonitorInfo monitor, PositionSettings position, AppearanceSettings appearance) =>
        InArea(monitor.WorkArea, monitor.Scale, position.BottomOffsetPercent, position.MaxWidthPercent, appearance.TextSize);

    /// <summary>At a spot the user dragged the captions to, at the chosen text size.</summary>
    public static CaptionLayout AtPoint(PixelPoint anchor, MonitorInfo monitor, PositionSettings position, AppearanceSettings appearance) =>
        new(anchor, monitor.WorkArea, monitor.Scale, appearance.TextSize, monitor.WorkArea.Width / monitor.Scale * position.MaxWidthPercent / 100);

    /// <summary>
    /// The chosen size at <see cref="ReferenceHeight"/>, growing and shrinking with the window.
    /// Kept between the Appearance minimum and maximum, widened if the chosen size lies outside them.
    /// </summary>
    public static double ScaledFontSize(double heightDips, AppearanceSettings appearance)
    {
        var size = appearance.TextSize * heightDips / ReferenceHeight;
        return Math.Clamp(size, Math.Min(appearance.FontSizeMin, appearance.TextSize), Math.Max(appearance.FontSizeMax, appearance.TextSize));
    }

    /// <summary>Top-left of a bubble of this size in physical pixels: bottom centre on the anchor, kept inside the bounds.</summary>
    public static PixelPoint Fit(CaptionLayout layout, int width, int height)
    {
        var bounds = layout.Bounds;
        var left = width >= bounds.Width
            ? bounds.Left + (bounds.Width - width) / 2
            : Math.Clamp(layout.Anchor.X - width / 2, bounds.Left, bounds.Right - width);
        var top = Math.Max(layout.Anchor.Y - height, bounds.Top);
        return new PixelPoint(left, top);
    }

    /// <summary>The band captions can fill: full maximum width, and as many lines as are allowed.</summary>
    public static PixelRect Band(CaptionLayout layout, int maxLines)
    {
        var width = (int)Math.Round(layout.MaxWidth * layout.Scale);
        var height = (int)Math.Round((layout.FontSize * LineHeight * Math.Max(1, maxLines) + BubblePadding) * layout.Scale);
        return new PixelRect(layout.Anchor.X - width / 2, layout.Anchor.Y - height, layout.Anchor.X - width / 2 + width, layout.Anchor.Y);
    }

    /// <summary>
    /// Captions are topmost, so without this they would float over a window placed on top of the
    /// video. Covered means a window above covers the anchor, or a third of the band.
    /// </summary>
    public static bool IsCovered(CaptionLayout layout, int maxLines, IReadOnlyList<PixelRect> above)
    {
        var band = Band(layout, maxLines);
        var point = new PixelPoint(layout.Anchor.X, layout.Anchor.Y - 1);
        return above.Any(window => window.Contains(point) || window.Intersect(band).Area * CoveredFraction >= band.Area);
    }

    private static CaptionLayout InArea(PixelRect area, double scale, int bottomPercent, int maxWidthPercent, double fontSize)
    {
        var anchor = new PixelPoint(area.Left + area.Width / 2, area.Bottom - (int)Math.Round(area.Height * bottomPercent / 100.0));
        return new CaptionLayout(anchor, area, scale, fontSize, area.Width / scale * maxWidthPercent / 100);
    }
}
