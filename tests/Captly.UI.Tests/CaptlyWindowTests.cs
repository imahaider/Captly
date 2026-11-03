using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Captly.UI.Controls;

namespace Captly.UI.Tests;

/// <summary>Real windows, placed far off screen so that nothing flashes up.</summary>
public class CaptlyWindowTests
{
    private const int WmNcHitTest = 0x0084;
    private const int HtMaxButton = 9;

    [DllImport("user32")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    private static CaptlyWindow Open(bool dialog = false)
    {
        var window = new CaptlyWindow
        {
            IsDialog = dialog,
            Width = 500,
            Height = 300,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        Ui.Settle();
        return window;
    }

    private static T Part<T>(CaptlyWindow window, string name) where T : FrameworkElement =>
        (T)window.Template.FindName(name, window);

    [Fact]
    public void MaximizeButton_ReportsHtMaxButton_SoSnapLayoutsOpen() => Ui.Run(() =>
    {
        var window = Open();
        var button = Part<Button>(window, "PART_Maximize");
        var center = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));

        var hit = SendMessage(new WindowInteropHelper(window).Handle, WmNcHitTest, 0, ((int)center.Y << 16) | ((int)center.X & 0xFFFF));

        Assert.Equal(HtMaxButton, hit);
        window.Close();
    });

    [Fact]
    public void Dialog_ShowsOnlyTheCloseButton() => Ui.Run(() =>
    {
        var window = Open(dialog: true);

        Assert.Equal(Visibility.Collapsed, Part<Button>(window, "PART_Minimize").Visibility);
        Assert.Equal(Visibility.Collapsed, Part<Button>(window, "PART_Maximize").Visibility);
        Assert.Equal(Visibility.Visible, Part<Button>(window, "PART_Close").Visibility);
        Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
        window.Close();
    });

    [Fact]
    public void Backdrop_IsMicaWhereWindowsHasIt_OrAnOpaqueToken() => Ui.Run(() =>
    {
        var window = Open();
        var mica = Environment.OSVersion.Version.Build >= 22621 && !SystemParameters.HighContrast;

        Assert.Equal(mica, window.IsMicaActive);
        Assert.Equal(mica, window.Background == Brushes.Transparent);
        window.Close();
    });

    [Fact]
    public void Maximizing_InsetsTheContent_AndRestoringRemovesIt() => Ui.Run(() =>
    {
        var window = Open();
        var root = Part<FrameworkElement>(window, "PART_Root");
        Assert.Equal(default, root.Margin);

        window.WindowState = WindowState.Maximized;
        Ui.Settle();
        Assert.True(root.Margin.Left > 0);

        window.WindowState = WindowState.Normal;
        Ui.Settle();
        Assert.Equal(default, root.Margin);
        window.Close();
    });

    [Fact]
    public void MaximizeButton_SwitchesToRestore() => Ui.Run(() =>
    {
        var window = Open();
        var button = Part<Button>(window, "PART_Maximize");

        window.WindowState = WindowState.Maximized;
        Ui.Settle();

        Assert.Equal("Restore", System.Windows.Automation.AutomationProperties.GetName(button));
        window.Close();
    });
}
