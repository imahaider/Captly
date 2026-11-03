using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Captly.UI.Controls;

/// <summary>
/// Base window: custom chrome with Mica, and Windows caption buttons. The maximize button reports
/// HTMAXBUTTON so that Snap Layouts open on hover. Windows then owns the mouse over that button,
/// so its hover and pressed looks are driven from non-client messages.
/// </summary>
[TemplatePart(Name = "PART_Root", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Minimize", Type = typeof(Button))]
[TemplatePart(Name = "PART_Maximize", Type = typeof(Button))]
[TemplatePart(Name = "PART_Close", Type = typeof(Button))]
public class CaptlyWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMaxButton = 9;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2;
    private const int MicaMinimumBuild = 22621;
    private const int SmCxPaddedBorder = 92;
    private const int SmCxSizeFrame = 32;
    private const uint TmeLeave = 0x2;
    private const uint TmeNonClient = 0x10;

    public static readonly DependencyProperty IsDialogProperty = DependencyProperty.Register(
        nameof(IsDialog), typeof(bool), typeof(CaptlyWindow), new PropertyMetadata(false));

    public static readonly DependencyProperty CaptionHeightProperty = DependencyProperty.Register(
        nameof(CaptionHeight), typeof(double), typeof(CaptlyWindow), new PropertyMetadata(32.0, (d, e) =>
        {
            if (WindowChrome.GetWindowChrome((Window)d) is { } chrome) chrome.CaptionHeight = (double)e.NewValue;
        }));

    private static readonly DependencyPropertyKey IsMicaActiveKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMicaActive), typeof(bool), typeof(CaptlyWindow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty IsMicaActiveProperty = IsMicaActiveKey.DependencyProperty;

    private FrameworkElement? _root;
    private Button? _maximize;
    private bool _trackingMouseLeave;

    static CaptlyWindow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(CaptlyWindow), new FrameworkPropertyMetadata(typeof(CaptlyWindow)));

    public CaptlyWindow()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = CaptionHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = default,
            UseAeroCaptionButtons = false,
            NonClientFrameEdges = NonClientFrameEdges.None,
        });
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => ThemeManager.ThemeChanged -= ApplyTheme;
        ThemeManager.ThemeChanged += ApplyTheme;
    }

    /// <summary>A dialog shows only the close button and does not resize.</summary>
    public bool IsDialog
    {
        get => (bool)GetValue(IsDialogProperty);
        set => SetValue(IsDialogProperty, value);
    }

    /// <summary>Height of the draggable band at the top. Controls inside it need IsHitTestVisibleInChrome.</summary>
    public double CaptionHeight
    {
        get => (double)GetValue(CaptionHeightProperty);
        set => SetValue(CaptionHeightProperty, value);
    }

    public bool IsMicaActive => (bool)GetValue(IsMicaActiveProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild("PART_Root") as FrameworkElement;
        _maximize = GetTemplateChild("PART_Maximize") as Button;
        if (GetTemplateChild("PART_Minimize") is Button minimize) minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        if (GetTemplateChild("PART_Close") is Button close) close.Click += (_, _) => Close();
        if (_maximize != null) _maximize.Click += (_, _) => ToggleMaximize();
        if (IsDialog) ResizeMode = ResizeMode.NoResize;
        UpdateMaximizeState();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateMaximizeState();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateMaximizeState();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;

        var dark = ThemeManager.Current == EffectiveTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var mica = ThemeManager.Current != EffectiveTheme.HighContrast && TryEnableMica(hwnd);
        SetValue(IsMicaActiveKey, mica);
        if (mica) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "Bg.Window");
    }

    private static bool TryEnableMica(nint hwnd)
    {
        if (Environment.OSVersion.Version.Build < MicaMinimumBuild) return false;

        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var backdrop = DwmsbtMainWindow;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaximizeState()
    {
        var maximized = WindowState == WindowState.Maximized;
        if (_maximize != null)
        {
            _maximize.Content = maximized ? "" : "";
            System.Windows.Automation.AutomationProperties.SetName(_maximize, maximized ? "Restore" : "Maximize");
        }

        // A maximized window hangs past the screen by its resize border.
        if (_root != null) _root.Margin = maximized ? MaximizedInset() : default;
    }

    private Thickness MaximizedInset()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var pixels = GetSystemMetricsForDpi(SmCxSizeFrame, (uint)dpi.PixelsPerInchX) + GetSystemMetricsForDpi(SmCxPaddedBorder, (uint)dpi.PixelsPerInchX);
        return new Thickness(pixels / dpi.DpiScaleX);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case WmNcHitTest when !IsDialog && IsOverMaximize(lParam):
                handled = true;
                return HtMaxButton;
            case WmNcMouseMove:
                SetMaximizeLook(wParam == HtMaxButton ? "Bg.Hover" : null);
                if (wParam == HtMaxButton) TrackMouseLeave(hwnd);
                break;
            case WmNcMouseLeave:
                _trackingMouseLeave = false;
                SetMaximizeLook(null);
                break;
            case WmNcLButtonDown when wParam == HtMaxButton:
                SetMaximizeLook("Bg.Pressed");
                handled = true;
                break;
            case WmNcLButtonUp when wParam == HtMaxButton:
                SetMaximizeLook(null);
                handled = true;
                ToggleMaximize();
                break;
        }
        return 0;
    }

    private bool IsOverMaximize(nint lParam)
    {
        if (_maximize is not { IsLoaded: true, IsVisible: true }) return false;
        var point = _maximize.PointFromScreen(new Point((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF)));
        return point.X >= 0 && point.Y >= 0 && point.X < _maximize.ActualWidth && point.Y < _maximize.ActualHeight;
    }

    private void SetMaximizeLook(string? brushKey)
    {
        if (_maximize == null) return;
        if (brushKey == null) _maximize.ClearValue(BackgroundProperty);
        else _maximize.SetResourceReference(BackgroundProperty, brushKey);
    }

    private void TrackMouseLeave(nint hwnd)
    {
        if (_trackingMouseLeave) return;
        var request = new TrackMouseEventRequest
        {
            Size = (uint)Marshal.SizeOf<TrackMouseEventRequest>(),
            Flags = TmeLeave | TmeNonClient,
            Window = hwnd,
        };
        _trackingMouseLeave = TrackMouseEvent(ref request);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventRequest
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }

    [DllImport("dwmapi")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32")] private static extern bool TrackMouseEvent(ref TrackMouseEventRequest request);
}
