using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Captly.Core.Settings;
using Captly.Core.Windowing;
using Captly.UI.Overlay;

namespace Captly.UI.Tests;

public class OverlayWindowTests
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExTopmost = 0x8;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32")] private static extern int GetWindowThreadProcessId(nint hwnd, out int pid);

    // Far off every screen, in physical pixels: 1000 x 600 with the anchor 10 % up from the bottom.
    private static readonly PixelRect OffscreenArea = PixelRect.FromSize(-20000, -20000, 1000, 600);
    private static readonly CaptionLayout OffscreenLayout = new(new PixelPoint(-19500, -19460), OffscreenArea, 1, 22, 800);

    private static OverlayWindow Offscreen(CaptionLayout? layout = null)
    {
        var overlay = new OverlayWindow { Layout = layout ?? OffscreenLayout };
        overlay.ShowLines(["The fan pulls in air"]);
        Ui.Settle();
        return overlay;
    }

    // The bubble's corners on screen, in physical pixels.
    private static (Point TopLeft, Point BottomRight) BubbleOnScreen(OverlayWindow overlay) =>
        (overlay.Bubble.PointToScreen(default), overlay.Bubble.PointToScreen(new Point(overlay.Bubble.ActualWidth, overlay.Bubble.ActualHeight)));

    private static long Styles(Window window) => GetWindowLongPtr(new WindowInteropHelper(window).Handle, GwlExStyle);

    [Fact]
    public void Window_HasTheOverlayStyles() => Ui.Run(() =>
    {
        var overlay = Offscreen();

        var styles = Styles(overlay);

        Assert.All(new[] { WsExTransparent, WsExToolWindow, WsExLayered, WsExNoActivate, WsExTopmost }, flag => Assert.NotEqual(0, styles & flag));
        overlay.Close();
    });

    [Fact]
    public void ClickThrough_CanBeTurnedOff() => Ui.Run(() =>
    {
        var overlay = Offscreen();

        overlay.ClickThrough = false;
        Assert.Equal(0, Styles(overlay) & WsExTransparent);

        overlay.ClickThrough = true;
        Assert.NotEqual(0, Styles(overlay) & WsExTransparent);
        overlay.Close();
    });

    // This uses Windows' own hit test on screen. The window is 2 % opaque, so it cannot be seen,
    // but is still solid enough to catch clicks when click-through is off. It sits mid-screen,
    // where a desktop has nothing on top. A build machine may, and then the test cannot tell.
    [Fact]
    public void Clicks_ReallyPassThrough() => Ui.Run(() =>
    {
        var middle = new PixelPoint(GetSystemMetrics(0) / 2, GetSystemMetrics(1) / 2);
        var area = new PixelRect(middle.X - 200, middle.Y - 150, middle.X + 200, middle.Y + 150);
        var window = new OverlayWindow { Layout = new CaptionLayout(new PixelPoint(middle.X, middle.Y + 130), area, 1, 22, 300), Opacity = 0.02 };
        window.Bubble.Background = Brushes.White;
        ((System.Windows.Controls.Border)window.Content).Background = Brushes.White;
        window.ShowLines(["Probe"]);
        Ui.Settle();
        var hwnd = new WindowInteropHelper(window).Handle;
        var center = window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2));
        var point = new NativePoint { X = (int)center.X, Y = (int)center.Y };

        window.ClickThrough = false;
        Ui.Settle();
        var solid = WindowFromPoint(point);

        window.ClickThrough = true;
        Ui.Settle();
        var through = WindowFromPoint(point);
        window.Close();

        GetWindowThreadProcessId(solid, out var owner);
        if (solid != hwnd && owner != Environment.ProcessId) Assert.Skip("Another app's window covers the middle of the screen, so the hit test cannot reach the probe.");
        Assert.Equal(hwnd, solid);
        Assert.NotEqual(hwnd, through);
    });

    [Fact]
    public void Caption_SitsOnTheAnchor() => Ui.Run(() =>
    {
        var overlay = Offscreen();

        var (topLeft, bottomRight) = BubbleOnScreen(overlay);

        Assert.Equal(-19500, (topLeft.X + bottomRight.X) / 2, 1.0);
        Assert.Equal(-19460, bottomRight.Y, 1.0);
        overlay.Close();
    });

    [Fact]
    public void Caption_StaysInsideTheBounds() => Ui.Run(() =>
    {
        var overlay = Offscreen(OffscreenLayout with { Anchor = new PixelPoint(-19990, -19995) });

        var (topLeft, _) = BubbleOnScreen(overlay);

        Assert.Equal(-20000, topLeft.X, 1.0);
        Assert.Equal(-20000, topLeft.Y, 1.0);
        overlay.Close();
    });

    [Fact]
    public void Caption_RespectsTheMaximumWidth() => Ui.Run(() =>
    {
        var overlay = new OverlayWindow { Layout = OffscreenLayout with { MaxWidth = 300 } };
        overlay.ShowLines(["A long caption that would be far too wide for this small maximum width if it were left alone"]);
        Ui.Settle();

        Assert.True(overlay.Bubble.ActualWidth <= 300);
        overlay.Close();
    });

    [Fact]
    public void Layout_SetsTheTextSize() => Ui.Run(() =>
    {
        var overlay = Offscreen(OffscreenLayout with { FontSize = 31 });

        Assert.Equal(31, overlay.Bubble.FontSize);
        overlay.Close();
    });

    [Fact]
    public void HiddenLayout_HidesTheCaption_UntilShownAgain() => Ui.Run(() =>
    {
        var overlay = Offscreen(OffscreenLayout with { Visible = false });
        Assert.False(overlay.IsVisible);

        overlay.Layout = OffscreenLayout;
        Ui.Settle();

        Assert.True(overlay.IsVisible);
        overlay.Close();
    });

    [Fact]
    public void HiddenLayout_WithNoText_StaysHidden() => Ui.Run(() =>
    {
        var overlay = new OverlayWindow { Layout = OffscreenLayout };

        overlay.Layout = OffscreenLayout with { FontSize = 30 };

        Assert.False(overlay.IsVisible);
        overlay.Close();
    });

    // Late updates can arrive while the app shuts down.
    [Fact]
    public void Updates_AfterClosing_AreIgnored() => Ui.Run(() =>
    {
        var overlay = Offscreen();
        overlay.Close();

        overlay.Layout = OffscreenLayout with { FontSize = 30 };
        overlay.ShowLines(["Late"]);
        overlay.ShowLines([]);
        Ui.Settle();

        Assert.False(overlay.IsVisible);
    });

    [Fact]
    public void FixedMode_AllowsMoving() => Ui.Run(() =>
    {
        var overlay = new OverlayWindow();

        overlay.Apply(new AppearanceSettings(), new PositionSettings { Mode = PositionMode.Fixed });
        Assert.True(overlay.AllowMove);

        overlay.Apply(new AppearanceSettings(), new PositionSettings());
        Assert.False(overlay.AllowMove);
        overlay.Close();
    });

    [Fact]
    public void Appearance_ReachesTheBubble() => Ui.Run(() =>
    {
        var overlay = new OverlayWindow();

        overlay.Apply(new AppearanceSettings { Style = CaptionStyle.Classic, TextSize = 30, TextColor = "#FFD60A", BackgroundOpacity = 0.4 }, new PositionSettings());

        Assert.Equal(CaptionStyle.Classic, overlay.Bubble.CaptionStyle);
        Assert.Equal(30, overlay.Bubble.FontSize);
        Assert.Equal(Color.FromRgb(0xFF, 0xD6, 0x0A), overlay.Bubble.TextColor);
        Assert.Equal(0.4, overlay.Bubble.BackgroundOpacity);
        overlay.Close();
    });

    [Fact]
    public void BadTextColour_FallsBackToWhite() => Ui.Run(() =>
    {
        var overlay = new OverlayWindow();

        overlay.Apply(new AppearanceSettings { TextColor = "not a colour" }, new PositionSettings());

        Assert.Equal(Colors.White, overlay.Bubble.TextColor);
        overlay.Close();
    });

    [Fact]
    public void Lines_ShowOneAboveTheOther() => Ui.Run(() =>
    {
        var overlay = Offscreen();

        overlay.ShowLines(["First line", "Second line"]);

        Assert.Equal("First line\nSecond line", overlay.Bubble.Text);
        overlay.Close();
    });
}
