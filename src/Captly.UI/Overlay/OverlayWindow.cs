using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Captly.Core.Settings;
using Captly.Core.Windowing;
using Captly.UI.Controls;

namespace Captly.UI.Overlay;

/// <summary>
/// Borderless, topmost, click-through window that holds the caption. It is only as big as the
/// caption, because a large per-pixel transparent window is costly to redraw.
/// It is placed in physical pixels with SetWindowPos, because WPF's Left and Top are scaled by the
/// DPI of whichever monitor the window was last on, which goes wrong across mixed-DPI monitors.
/// </summary>
public sealed class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int VkControl = 0x11;
    private static readonly nint HwndTopmost = -1;

    // Room around the bubble for its shadow.
    private const double ShadowRoom = 24;

    private static readonly Duration FadeTime = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan CtrlPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly CaptionBubble _bubble = new();
    private readonly Rectangle _moveOutline = new()
    {
        Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1,
        StrokeDashArray = [4, 3],
        RadiusX = 12,
        RadiusY = 12,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };
    private readonly DispatcherTimer _ctrlTimer;
    private CaptionLayout _layout = DefaultLayout();
    private bool _clickThrough = true;
    private bool _allowMove;
    private bool _moving;
    private bool _hasText;
    private bool _closed;
    private bool _suppressed;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Captly captions";

        _bubble.Opacity = 0;
        _bubble.FontSize = _layout.FontSize;
        _bubble.MaxWidth = _layout.MaxWidth;
        Content = new Border { Padding = new Thickness(ShadowRoom), Child = new Grid { Children = { _bubble, _moveOutline } } };

        _ctrlTimer = new DispatcherTimer(CtrlPollInterval, DispatcherPriority.Input, (_, _) => CheckCtrl(), Dispatcher) { IsEnabled = false };
        SourceInitialized += (_, _) => ApplyWindowStyles();
        SizeChanged += (_, _) => Place();
        DpiChanged += (_, _) => Place();
        IsVisibleChanged += (_, _) => UpdateCtrlTimer();
        MouseLeftButtonDown += (_, _) => DragWhileMoving();
        Closed += (_, _) =>
        {
            _closed = true;
            _ctrlTimer.Stop();
        };
    }

    public CaptionBubble Bubble => _bubble;

    /// <summary>Raised after a Ctrl-drag, with the bubble's new bottom centre in physical pixels.</summary>
    public event Action<PixelPoint>? Moved;

    /// <summary>When on, the mouse reaches whatever is under the captions.</summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            ApplyWindowStyles();
        }
    }

    /// <summary>Lets Ctrl and drag move the captions. Used in the fixed-spot mode only.</summary>
    public bool AllowMove
    {
        get => _allowMove;
        set
        {
            _allowMove = value;
            UpdateCtrlTimer();
        }
    }

    /// <summary>Where the caption sits, how big its text is, and whether it may show at all.</summary>
    public CaptionLayout Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            _bubble.FontSize = value.FontSize;
            _bubble.MaxWidth = value.MaxWidth;
            UpdateVisibility();
            Place();
        }
    }

    /// <summary>"Hide captions": the text keeps updating, but nothing shows until this is turned off.</summary>
    public bool IsSuppressed
    {
        get => _suppressed;
        set
        {
            _suppressed = value;
            UpdateVisibility();
        }
    }

    /// <summary>The look of the captions. Their place and text size come from <see cref="Layout"/>.</summary>
    public void Apply(AppearanceSettings appearance, PositionSettings position)
    {
        _bubble.CaptionStyle = appearance.Style;
        _bubble.FontSize = appearance.TextSize;
        _bubble.BackgroundOpacity = appearance.BackgroundOpacity;
        _bubble.TextColor = ParseColor(appearance.TextColor);
        ClickThrough = position.ClickThrough;
        AllowMove = position.Mode == PositionMode.Fixed;
    }

    /// <summary>Shows these lines. An empty list fades the caption out, then hides the window.</summary>
    public void ShowLines(IReadOnlyList<string> lines)
    {
        _hasText = lines.Count > 0;
        if (!_hasText)
        {
            Fade(0, () =>
            {
                if (!_hasText && !_closed) Hide();
            });
            return;
        }

        _bubble.Text = string.Join('\n', lines);
        UpdateVisibility();
    }

    /// <summary>Puts the captions back on top, after another window came to the front.</summary>
    public void BringToTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0 && IsVisible) SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    // A closed window cannot be shown again, and late updates can arrive during shutdown.
    private void UpdateVisibility()
    {
        if (_closed) return;
        if (!_layout.Visible || _suppressed)
        {
            _bubble.BeginAnimation(OpacityProperty, null);
            _bubble.Opacity = 0;
            Hide();
            return;
        }
        if (!_hasText) return;

        // Fading in starts from the current opacity, so updates do not flash. A caption already
        // fully shown is not animated, because every animation frame redraws the whole window.
        if (!IsVisible) Show();
        if (_bubble.Opacity < 1) Fade(1, null);
    }

    private void Fade(double to, Action? then)
    {
        var duration = SystemParameters.ClientAreaAnimation ? FadeTime : new Duration(TimeSpan.Zero);
        var animation = new DoubleAnimation(to, duration);
        if (then != null) animation.Completed += (_, _) => then();
        _bubble.BeginAnimation(OpacityProperty, animation);
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var shadow = (int)Math.Round(ShadowRoom * scale);
        var bubble = CaptionPlacement.Fit(_layout, (int)Math.Round(_bubble.ActualWidth * scale), (int)Math.Round(_bubble.ActualHeight * scale));
        SetWindowPos(hwnd, 0, bubble.X - shadow, bubble.Y - shadow, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        var style = GetWindowLongPtr(hwnd, GwlExStyle) | WsExToolWindow | WsExLayered | WsExNoActivate;
        style = _clickThrough && !_moving ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(hwnd, GwlExStyle, (nint)style);
    }

    private void UpdateCtrlTimer()
    {
        _ctrlTimer.IsEnabled = _allowMove && IsVisible;
        if (!_ctrlTimer.IsEnabled) SetMoving(false);
    }

    // A click-through window never sees the keyboard or mouse, so Ctrl is polled. While it is held
    // over the captions, they catch the mouse, show a dashed outline and can be dragged.
    private void CheckCtrl()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
        var over = GetCursorPos(out var cursor) && GetWindowRect(hwnd, out var rect)
            && cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
        SetMoving(ctrl && over);
    }

    private void SetMoving(bool moving)
    {
        if (_moving == moving) return;
        _moving = moving;
        _moveOutline.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
        Cursor = moving ? Cursors.SizeAll : null;
        ApplyWindowStyles();
    }

    private void DragWhileMoving()
    {
        if (!_moving) return;
        DragMove();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(hwnd, out var rect)) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var shadow = (int)Math.Round(ShadowRoom * scale);
        var anchor = new PixelPoint(rect.Left + shadow + (int)Math.Round(_bubble.ActualWidth * scale / 2), rect.Top + shadow + (int)Math.Round(_bubble.ActualHeight * scale));
        _layout = _layout with { Anchor = anchor };
        Moved?.Invoke(anchor);
    }

    // The primary screen's work area, until the app supplies a layout.
    private static CaptionLayout DefaultLayout()
    {
        var scale = GetDpiForSystem() / 96.0;
        var work = SystemParameters.WorkArea;
        var area = new PixelRect((int)(work.Left * scale), (int)(work.Top * scale), (int)(work.Right * scale), (int)(work.Bottom * scale));
        return CaptionPlacement.OnScreen(new MonitorInfo(area, area, scale), new PositionSettings(), new AppearanceSettings());
    }

    private static Color ParseColor(string text)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch (FormatException)
        {
            return Colors.White;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] private static extern uint GetDpiForSystem();
}
