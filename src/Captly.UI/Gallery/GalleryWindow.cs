using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Captly.UI.Controls;

namespace Captly.UI.Gallery;

/// <summary>Every control in light and dark, side by side, for review against the design images.</summary>
public sealed class GalleryWindow : CaptlyWindow
{
    public GalleryWindow()
    {
        Title = "Captly gallery";
        Width = 1240;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panes = new UniformGrid2();
        foreach (var theme in new[] { EffectiveTheme.Light, EffectiveTheme.Dark })
        {
            var pane = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 32, 0, 0), Content = new GalleryContent() };
            pane.Resources.MergedDictionaries.Add(ThemeManager.LoadTokens(theme));
            panes.Children.Add(pane);
        }
        Content = panes;
    }

    private sealed class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid2() => Rows = 1;
    }
}

/// <summary>Writes gallery PNGs without opening a window, so the look can be checked unattended.</summary>
public static class GalleryRenderer
{
    private const double GalleryWidth = 620;
    private const double ShellWidth = 920;
    private const double ShellHeight = 640;
    private const double BaseDpi = 96;

    public static void Render(string folder, double scale)
    {
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { EffectiveTheme.Light, EffectiveTheme.Dark })
        {
            var gallery = new GalleryContent();
            Write(Host(gallery, theme, new Size(GalleryWidth, double.PositiveInfinity), recording: true), Path.Combine(folder, $"gallery-{theme}.png"), scale);

            foreach (var page in GalleryShell.PageNames)
            {
                var shell = Host(new GalleryShell(page), theme, new Size(ShellWidth, ShellHeight), recording: false);
                Write(shell, Path.Combine(folder, $"{page.Replace(" ", "")}-{theme}.png"), scale);
            }
        }
    }

    private static FrameworkElement Host(FrameworkElement content, EffectiveTheme theme, Size size, bool recording)
    {
        content.Resources.MergedDictionaries.Add(ThemeManager.LoadTokens(theme));
        TextOptions.SetTextFormattingMode(content, TextFormattingMode.Ideal);
        content.Measure(size);
        var height = double.IsInfinity(size.Height) ? content.DesiredSize.Height : size.Height;
        content.Arrange(new Rect(0, 0, size.Width, height));
        content.UpdateLayout();
        if (recording && content is GalleryContent gallery) gallery.ShowRecording();
        content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, () => { });
        return content;
    }

    private static void Write(FrameworkElement content, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * scale), (int)(content.ActualHeight * scale), BaseDpi * scale, BaseDpi * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
