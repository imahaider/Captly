using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Captly.Core.Audio;
using Captly.Core.Media;
using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;
using Captly.UI;
using Captly.UI.Popover;
using Captly.UI.Settings;
using Captly.UI.Setup;
using Captly.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace Captly.App;

/// <summary>
/// Draws the real Settings screens and the popover to PNG, in light and dark, with sample state
/// close to the design images. It opens nothing on screen that stays, so the look can be checked
/// unattended against `Captly UI-png`.
/// </summary>
internal static class UiRenderer
{
    private const double BaseDpi = 96;
    private static readonly Size SettingsSize = new(920, 640);

    private static readonly string[] Pages = ["General", "Source", "Appearance", "Position", "Speech Model", "Transcripts", "Shortcuts", "About"];

    private sealed class NoShell : IShellServices
    {
        public void OpenSettings(string? page = null) { }
        public void OpenSetup() { }
        public void Open(string target) { }
        public string? PickFolder(string current) => null;
        public void Quit() { }
    }

    private sealed class NoCaptioning : ICaptioning
    {
        public void SetOn(bool on) { }
        public void SetHidden(bool hidden) { }
        public void NextSource() { }
    }

    // The real catalogue, with only Tiny installed. A held download stops at 42 %, as in the design.
    private sealed class SampleModels(IModelManager real, bool holdDownloads) : IModelManager
    {
        private readonly HashSet<string> _installed = [real.Available.MinBy(m => m.MillionParameters)!.Id];

        public IReadOnlyList<ModelInfo> Available => real.Available.Select(m => m with { IsInstalled = _installed.Contains(m.Id) }).ToList();

        public string GetModelPath(string id) => real.GetModelPath(id);

        public async Task DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct)
        {
            progress?.Report(0.42);
            if (holdDownloads) await Task.Delay(Timeout.Infinite, ct);
            _installed.Add(id);
        }
    }

    private sealed class UpToDate : IUpdateChecker
    {
        public Task<UpdateCheck> CheckAsync(CancellationToken ct) => Task.FromResult(new UpdateCheck(UpdateOutcome.UpToDate));
    }

    public static void Render(ServiceProvider services, string folder, double scale, string version)
    {
        Directory.CreateDirectory(folder);
        var settings = new SettingsService(new AppSettings(), _ => { }, TimeSpan.Zero);
        var state = SampleState();
        var updates = new UpdateStatus(new UpToDate(), version, _ => { });
        updates.CheckAsync().GetAwaiter().GetResult();
        var models = services.GetRequiredService<IModelManager>();
        var shell = new NoShell();
        var model = new SettingsModel(
            new GeneralPageModel(settings, updates),
            new SourcePageModel(settings, state),
            new AppearancePageModel(settings),
            new PositionPageModel(settings),
            new SpeechPageModel(settings, models, (m, _) => Task.FromResult(new BenchResult(m.Id, m.MillionParameters, 0.3)), GraphicsAdapters.List()),
            new TranscriptsPageModel(settings, shell),
            new ShortcutsPageModel(settings),
            new AboutPageModel(updates, _ => { }));

        // Closing a window could otherwise end the app, and its resources with it.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        EffectiveTheme[] themes = [EffectiveTheme.Light, EffectiveTheme.Dark];
        foreach (var theme in themes)
        {
            var view = new SettingsView(model);
            view.Resources.MergedDictionaries.Add(ThemeManager.LoadTokens(theme));
            TextOptions.SetTextFormattingMode(view, TextFormattingMode.Ideal);
            foreach (var page in Pages)
            {
                view.ShowPage(page);
                Layout(view, SettingsSize);
                Write(view, Path.Combine(folder, $"Settings-{page.Replace(" ", "")}-{theme}.png"), scale);
            }
        }

        foreach (var theme in themes)
        {
            // A fast PC, mid-download of Small, for steps 1 and 2; Tiny ready, for step 3.
            var downloading = new SetupModel(settings, new SampleModels(models, holdDownloads: true), (m, _) => Task.FromResult(new BenchResult(m.Id, m.MillionParameters, 0.2)));
            var window = ShowOffscreen(new SetupWindow(downloading), theme);
            Write(window, Path.Combine(folder, $"Setup-1-Welcome-{theme}.png"), scale, (Brush)window.FindResource("Bg.Card"));
            downloading.ContinueCommand.Execute(null);
            Settle(20);
            Write(window, Path.Combine(folder, $"Setup-2-Model-{theme}.png"), scale, (Brush)window.FindResource("Bg.Card"));
            window.Close();

            var ready = new SetupModel(settings, new SampleModels(models, holdDownloads: false), (m, _) => Task.FromResult(new BenchResult(m.Id, m.MillionParameters, 0.31)));
            window = ShowOffscreen(new SetupWindow(ready), theme);
            ready.ContinueCommand.Execute(null);
            Settle(20);
            ready.ContinueCommand.Execute(null);
            Settle(5);
            Write(window, Path.Combine(folder, $"Setup-3-Done-{theme}.png"), scale, (Brush)window.FindResource("Bg.Card"));
            window.Close();
        }

        foreach (var theme in themes)
        {
            var popover = new PopoverWindow(new PopoverModel(state, settings, new NoCaptioning(), shell))
            {
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
            };
            popover.Resources.MergedDictionaries.Add(ThemeManager.LoadTokens(theme));
            popover.Show();
            Settle();
            Write(popover, Path.Combine(folder, $"Popover-{theme}.png"), scale);
            popover.Close();
        }
    }

    private static AppState SampleState()
    {
        var chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        var chrome = new AudioAppInfo(100, "chrome.exe", "Google Chrome", File.Exists(chromePath) ? chromePath : null, 0.7f, null);
        var spotify = new AudioAppInfo(200, "Spotify.exe", "Spotify", null, 0.5f, null);
        var vlc = new AudioAppInfo(300, "vlc.exe", "VLC media player", null, 0, null);
        var media = new MediaState("Chrome", "Inside a jet engine", "", PlaybackStatus.Playing, TimeSpan.FromSeconds(761), TimeSpan.FromSeconds(1685), default, true);
        return new AppState
        {
            IsOn = true,
            Status = CaptionStatus.Captioning,
            Target = CaptureTarget.For(chrome),
            Apps = [chrome, spotify, vlc],
            Sessions = [media],
            Media = media,
            Position = media.Position,
            ModelName = "Tiny model",
            LatestCaption = "…and most of that air flows around the core, which is where the thrust really comes from.",
        };
    }

    private static void Layout(FrameworkElement content, Size size)
    {
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
        Settle();
        content.UpdateLayout();
    }

    private static void Settle() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, () => { });

    // Lets background steps, such as a speed test, come back to the UI thread.
    private static void Settle(int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            Settle();
            Thread.Sleep(15);
        }
    }

    private static Window ShowOffscreen(Window window, EffectiveTheme theme)
    {
        window.Left = -20000;
        window.Top = -20000;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowActivated = false;
        window.Resources.MergedDictionaries.Add(ThemeManager.LoadTokens(theme));
        window.Show();
        Settle(3);
        return window;
    }

    // A window with Mica has no background of its own, so one can be painted behind it.
    private static void Write(FrameworkElement content, string path, double scale, Brush? background = null)
    {
        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * scale), (int)(content.ActualHeight * scale), BaseDpi * scale, BaseDpi * scale, PixelFormats.Pbgra32);
        if (background != null)
        {
            var fill = new DrawingVisual();
            using (var drawing = fill.RenderOpen()) drawing.DrawRectangle(background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            bitmap.Render(fill);
        }
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
