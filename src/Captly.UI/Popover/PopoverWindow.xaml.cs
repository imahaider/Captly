using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Captly.Core.Shell;
using Captly.Core.Windowing;

namespace Captly.UI.Popover;

/// <summary>
/// Opens above the tray icon and closes when it loses focus or on Esc, like Windows' own flyouts.
/// It is hidden, not closed, so it opens again at once.
/// </summary>
public partial class PopoverWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    // Space between the popover and the taskbar, as Windows leaves for its flyouts.
    private const double Gap = 12;

    public PopoverWindow(PopoverModel model)
    {
        InitializeComponent();
        DataContext = model;
        SourceInitialized += (_, _) =>
        {
            var corner = DwmwcpRound;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        };
        Deactivated += (_, _) => Dismiss();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Dismiss();
            e.Handled = true;
        };
    }

    /// <summary>When it last closed. A tray click that closed it should not open it again straight away.</summary>
    public DateTime LastHidden { get; private set; }

    /// <summary>Shows it at the bottom-right corner of this work area, in physical pixels at the monitor's scale.</summary>
    public void ShowAt(PixelRect workArea, double scale)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        var gap = (int)Math.Round(Gap * scale);
        var x = workArea.Right - width - gap;
        var y = workArea.Bottom - height - gap;

        SetWindowPos(hwnd, 0, x, y, width, height, SwpNoZOrder | SwpNoActivate);
        Show();
        // A move onto a monitor with another scale resizes the window, so place it once more.
        SetWindowPos(hwnd, 0, x, y, width, height, SwpNoZOrder | SwpNoActivate);
        Activate();
        Toggle.Focus();
    }

    private void Dismiss()
    {
        if (!IsVisible) return;
        LastHidden = DateTime.UtcNow;
        Hide();
    }

    [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
