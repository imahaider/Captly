using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;
using Captly.UI.Controls;
using Captly.UI.Popover;
using Captly.UI.Settings;
using Captly.UI.Shell;

namespace Captly.UI.Tests;

internal sealed class NoModels : IModelManager
{
    public IReadOnlyList<ModelInfo> Available { get; } = [new("moonshine-tiny-streaming", "Tiny Streaming", 34, 0, ModelArchitecture.TinyStreaming, true)];
    public string GetModelPath(string id) => id;
    public Task DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class NoShell : IShellServices
{
    public void OpenSettings(string? page = null) { }
    public void OpenSetup() { }
    public void Open(string target) { }
    public string? PickFolder(string current) => null;
    public void Quit() { }
}

internal sealed class NoChecker : IUpdateChecker
{
    public Task<UpdateCheck> CheckAsync(CancellationToken ct) => Task.FromResult(new UpdateCheck(UpdateOutcome.UpToDate));
}

internal sealed class NoCaptioning : ICaptioning
{
    public void SetOn(bool on) { }
    public void SetHidden(bool hidden) { }
    public void NextSource() { }
}

public class ShellViewTests
{
    private static (SettingsView View, SettingsService Settings) Build()
    {
        var settings = new SettingsService(new AppSettings(), _ => { }, TimeSpan.Zero);
        var state = new AppState();
        var updates = new UpdateStatus(new NoChecker(), "0.1.0", _ => { });
        var model = new SettingsModel(
            new GeneralPageModel(settings, updates), new SourcePageModel(settings, state), new AppearancePageModel(settings), new PositionPageModel(settings),
            new SpeechPageModel(settings, new NoModels(), (m, _) => Task.FromResult(new BenchResult(m.Id, 34, 0.3)), []),
            new TranscriptsPageModel(settings, new NoShell()), new ShortcutsPageModel(settings), new AboutPageModel(updates, _ => { }));
        var view = new SettingsView(model);
        var host = new Border { Child = view };
        host.Measure(new Size(920, 640));
        host.Arrange(new Rect(0, 0, 920, 640));
        Ui.Settle();
        return (view, settings);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var inner in Find<T>(child)) yield return inner;
        }
    }

    // IsVisible needs a window. Off screen, a row shows when it and every parent are Visible.
    private static bool IsShown(DependencyObject element)
    {
        for (var e = element; e != null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is UIElement { Visibility: not Visibility.Visible }) return false;
        }
        return true;
    }

    [Fact]
    public void AllEightPages_AreThere_InOrder() => Ui.Run(() =>
    {
        var (view, _) = Build();

        Assert.Equal(["General", "Source", "Appearance", "Position", "Speech Model", "Transcripts", "Shortcuts", "About"], view.SettingsShell.Pages.Select(p => p.Title));
        Assert.True(view.SettingsShell.Pages[^1].IsPinned);
        Assert.Equal("About Captly", view.SettingsShell.Pages[^1].NavTitle);
    });

    [Fact]
    public void Toggles_WriteTheSettings() => Ui.Run(() =>
    {
        var (view, settings) = Build();
        var pause = Find<SettingRow>(view).First(r => r.Label == "Pause captions when the video pauses");

        ((ToggleSwitch)pause.Content).IsChecked = false;

        Assert.False(settings.Current.General.PauseWithVideo);
    });

    [Fact]
    public void ShowPage_SelectsByTitle() => Ui.Run(() =>
    {
        var (view, _) = Build();

        view.ShowPage("speech model");

        Assert.Equal("Speech Model", view.SettingsShell.SelectedPage!.Title);
    });

    [Fact]
    public void Search_KeepsMatchingPages_AndHidesOtherRows() => Ui.Run(() =>
    {
        var (view, _) = Build();
        var shell = view.SettingsShell;

        shell.SearchText = "pause";
        Ui.Settle();
        shell.FilterRows();

        var shown = System.Windows.Data.CollectionViewSource.GetDefaultView(shell.Pages).Cast<SettingsPage>().Select(p => p.Title).ToList();
        Assert.Equal(["General"], shown);
        var rows = Find<SettingRow>(view).Where(IsShown).Select(r => r.Label).ToList();
        Assert.Equal(["Pause captions when the video pauses"], rows);

        shell.SearchText = "";
        Ui.Settle();
        shell.FilterRows();
        Assert.Contains("Theme", Find<SettingRow>(view).Where(IsShown).Select(r => r.Label));
    });

    [Fact]
    public void Search_FindsShortcutsByTheirLabels_AndMovesThere() => Ui.Run(() =>
    {
        var (view, _) = Build();

        view.SettingsShell.SearchText = "next app";

        Assert.Equal("Shortcuts", view.SettingsShell.SelectedPage!.Title);
    });

    [Fact]
    public void Search_WithNoMatch_KeepsThePageShown() => Ui.Run(() =>
    {
        var (view, _) = Build();

        view.SettingsShell.SearchText = "zzzz";
        Ui.Settle();

        Assert.Equal("General", view.SettingsShell.SelectedPage!.Title);
    });

    [Fact]
    public void Matches_ReadsLabelsOfPagesNotShown() => Ui.Run(() =>
    {
        var (view, _) = Build();
        var position = view.SettingsShell.Pages.Single(p => p.Title == "Position");

        Assert.True(SettingsShell.Matches(position, "fullscreen"));
        Assert.True(SettingsShell.Matches(position, "Fixed spot"));
        Assert.False(SettingsShell.Matches(position, "transcript"));
    });

    [Fact]
    public void ToggleSwitch_SetBeforeShowing_StandsInPlace() => Ui.Run(() =>
    {
        var toggle = new ToggleSwitch { Width = 40, Height = 24, IsChecked = false };
        var host = new Border { Child = toggle };
        host.Measure(new Size(100, 100));
        host.Arrange(new Rect(0, 0, 100, 100));

        toggle.IsChecked = true;
        Ui.Settle();

        var knob = Find<FrameworkElement>(toggle).First(e => e.RenderTransform is TranslateTransform);
        Assert.Equal(16, ((TranslateTransform)knob.RenderTransform).X);
    });

    [Fact]
    public void Popover_ShowsTheState() => Ui.Run(() =>
    {
        var state = new AppState { IsOn = true, Status = CaptionStatus.Waiting, ModelName = "Tiny model" };
        var settings = new SettingsService(new AppSettings(), _ => { }, TimeSpan.Zero);
        var popover = new PopoverWindow(new PopoverModel(state, settings, new NoCaptioning(), new NoShell())) { Left = -20000, Top = -20000, ShowActivated = false };
        popover.Show();
        Ui.Settle();

        var texts = Find<TextBlock>(popover).Select(t => t.Text).ToList();
        popover.Close();

        Assert.Contains("Waiting for a video", texts);
        Assert.Contains("Nothing playing", texts);
        Assert.Contains("Processor", texts);
        Assert.Contains("Tiny model", texts);
    });

    [Fact]
    public void Hotkeys_HeldByOneOwner_AreRefusedToTheNext() => Ui.Run(() =>
    {
        var keys = new HotkeySettings { Toggle = "Ctrl+Alt+Shift+F20", Hide = "Ctrl+Alt+Shift+F21", NextSource = "Ctrl+Alt+Shift+F22", OpenTranscript = "Ctrl+Alt+Shift+F23", OpenSettings = "Ctrl+Alt+Shift+F24" };
        using var first = new GlobalHotkeys();
        using var second = new GlobalHotkeys();

        Assert.Empty(first.Apply(keys));
        Assert.Equal(5, second.Apply(keys).Count);

        first.Dispose();
        Assert.Empty(second.Apply(keys));
    });

    [Theory]
    [InlineData(TrayState.On, true, "CaptlyTray-on-light-taskbar.ico")]
    [InlineData(TrayState.Waiting, false, "CaptlyTray-waiting-dark-taskbar.ico")]
    [InlineData(TrayState.Off, true, "CaptlyTray-off-light-taskbar.ico")]
    public void TrayIcons_ExistForEveryState(TrayState state, bool light, string expected) => Ui.Run(() =>
    {
        var name = TrayIcon.IconName(state, light);
        Assert.Equal(expected, name);
        var icon = BitmapFrame.Create(new Uri($"pack://application:,,,/Captly.UI;component/Tray/{name}"));
        Assert.True(icon.PixelWidth >= 16);
    });

    [Fact]
    public void AppIcons_ComeFromTheExe() => Ui.Run(() =>
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Assert.NotNull(AppIcons.For(exe));
        Assert.Null(AppIcons.For(null));
    });
}
