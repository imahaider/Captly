using System.ComponentModel;
using Captly.Core.Audio;
using Captly.Core.Media;
using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;
using Captly.Core.Transcripts;

namespace Captly.Core.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void Update_AppliesAtOnce_AndRaisesOldAndNew()
    {
        var service = Services.Settings();
        (AppSettings Old, AppSettings New)? seen = null;
        service.Changed += (o, n) => seen = (o, n);

        service.Update(s => s with { General = s.General with { PauseWithVideo = false } });

        Assert.False(service.Current.General.PauseWithVideo);
        Assert.True(seen!.Value.Old.General.PauseWithVideo);
        Assert.False(seen.Value.New.General.PauseWithVideo);
    }

    [Fact]
    public void Update_WithNoRealChange_DoesNothing()
    {
        var saved = new List<AppSettings>();
        var service = Services.Settings(saved);
        var raised = false;
        service.Changed += (_, _) => raised = true;

        service.Update(s => s with { General = s.General with { PauseWithVideo = true } });

        Assert.False(raised);
        Assert.Empty(saved);
    }

    [Fact]
    public void Saves_AreDelayed_AndBatched()
    {
        var saved = new List<AppSettings>();
        using var service = new SettingsService(new AppSettings(), saved.Add, TimeSpan.FromHours(1));

        for (var size = 20; size < 30; size++) service.Update(s => s with { Appearance = s.Appearance with { TextSize = size } });
        Assert.Empty(saved);

        service.Flush();
        Assert.Equal(29, Assert.Single(saved).Appearance.TextSize);
    }

    [Fact]
    public void Dispose_SavesWhatIsLeft()
    {
        var saved = new List<AppSettings>();
        var service = new SettingsService(new AppSettings(), saved.Add, TimeSpan.FromHours(1));
        service.Update(s => s with { SetupCompleted = true });

        service.Dispose();

        Assert.True(Assert.Single(saved).SetupCompleted);
    }
}

public class HotkeysTests
{
    private static readonly HotkeySettings Defaults = new();

    [Fact]
    public void GetAndWith_CoverEveryAction()
    {
        foreach (var (action, _) in Hotkeys.All)
        {
            var changed = Hotkeys.With(Defaults, action, "Ctrl+Shift+F9");
            Assert.Equal("Ctrl+Shift+F9", Hotkeys.Get(changed, action));
        }
    }

    [Fact]
    public void Conflict_NamesTheOtherAction()
    {
        Assert.Equal("Already used to show or hide captions.", Hotkeys.Conflict(Defaults, HotkeyAction.Toggle, "ctrl + alt + h"));
        Assert.Equal("Already used to open Captly settings.", Hotkeys.Conflict(Defaults, HotkeyAction.Toggle, "Ctrl+Alt+O"));
    }

    [Fact]
    public void Conflict_IgnoresTheActionItself_AndFreeKeys()
    {
        Assert.Null(Hotkeys.Conflict(Defaults, HotkeyAction.Toggle, "Ctrl+Alt+C"));
        Assert.Null(Hotkeys.Conflict(Defaults, HotkeyAction.Toggle, "Ctrl+Alt+K"));
    }

    [Fact]
    public void Display_SpacesThePlusSigns() => Assert.Equal("Ctrl + Alt + H", Hotkeys.Display("Ctrl+Alt+H"));

    [Theory]
    [InlineData("Ctrl+Alt+C", 0x3u, 0x43u)]
    [InlineData("Ctrl+Shift+F9", 0x6u, 0x78u)]
    [InlineData("Win+Num5", 0x8u, 0x65u)]
    [InlineData("Alt+7", 0x1u, 0x37u)]
    [InlineData("Ctrl+Alt+PageDown", 0x3u, 0x22u)]
    [InlineData("F24", 0x0u, 0x87u)]
    public void Parse_GivesWin32Codes(string shortcut, uint modifiers, uint key) =>
        Assert.Equal((modifiers, key), Hotkeys.Parse(shortcut));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Escape")]
    [InlineData("F25")]
    public void Parse_RejectsWhatCannotBeAShortcut(string? shortcut) => Assert.Null(Hotkeys.Parse(shortcut));

    [Fact]
    public void Parse_ReadsEveryDefault()
    {
        foreach (var (action, _) in Hotkeys.All) Assert.NotNull(Hotkeys.Parse(Hotkeys.Get(Defaults, action)));
    }
}

public class PageModelTests
{
    private static List<string> Raised(INotifyPropertyChanged model)
    {
        var names = new List<string>();
        model.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? "");
        return names;
    }

    [Fact]
    public void General_WritesAndRereadsEveryOption()
    {
        var saved = new List<AppSettings>();
        var settings = Services.Settings(saved);
        var page = new GeneralPageModel(settings, new UpdateStatus(new FakeChecker(), "0.1.0", _ => { }));
        var raised = Raised(page);

        page.StartWithWindows = true;
        page.AutoCaption = false;
        page.PauseWithVideo = false;
        page.ThemeIndex = 1;
        page.CheckUpdates = false;

        var g = settings.Current.General;
        Assert.True(g.StartWithWindows);
        Assert.False(g.AutoCaption);
        Assert.False(g.PauseWithVideo);
        Assert.Equal(ThemeChoice.Dark, g.Theme);
        Assert.False(g.CheckUpdates);
        Assert.Equal(1, page.ThemeIndex);
        Assert.Equal(5, saved.Count);
        Assert.Contains("", raised);
        Assert.Equal("Captly 0.1.0", page.VersionLabel);
    }

    [Theory]
    [InlineData(0, ThemeChoice.Light)]
    [InlineData(1, ThemeChoice.Dark)]
    [InlineData(2, ThemeChoice.System)]
    public void General_ThemeSegments(int index, ThemeChoice theme)
    {
        var settings = Services.Settings();
        var page = new GeneralPageModel(settings, new UpdateStatus(new FakeChecker(), "0.1.0", _ => { })) { ThemeIndex = index };
        Assert.Equal(theme, settings.Current.General.Theme);
    }

    [Fact]
    public void Appearance_PresetsSwatchesAndSliders()
    {
        var settings = Services.Settings();
        var page = new AppearancePageModel(settings);
        Assert.True(page.IsGlass);
        Assert.True(page.IsWhite);

        page.IsClassic = true;
        page.IsYellow = true;
        page.TextSize = 27.6;
        page.BackgroundPercent = 40.4;
        page.Lines = page.LineChoices[2];
        page.HideAfter = page.HideAfterChoices[2];

        var a = settings.Current.Appearance;
        Assert.Equal(CaptionStyle.Classic, a.Style);
        Assert.False(page.IsGlass);
        Assert.Equal(AppearancePageModel.Yellow, a.TextColor);
        Assert.Equal(28, a.TextSize);
        Assert.Equal(0.4, a.BackgroundOpacity);
        Assert.Equal(3, a.MaxLines);
        Assert.Equal(8000, a.ClearAfterMs);
        Assert.Equal("8 seconds", page.HideAfter.Label);
    }

    [Fact]
    public void Appearance_UncheckingARadio_ChangesNothing()
    {
        var settings = Services.Settings();
        var page = new AppearancePageModel(settings) { IsGlass = false, IsWhite = false };

        Assert.Equal(CaptionStyle.Glass, settings.Current.Appearance.Style);
        Assert.Equal(AppearancePageModel.White, settings.Current.Appearance.TextColor);
    }

    [Fact]
    public void Position_CardsSlidersAndSelect()
    {
        var settings = Services.Settings();
        var page = new PositionPageModel(settings) { IsFixed = true, BottomOffset = 20, MaxWidth = 60, ClickThrough = false };
        page.Fullscreen = page.FullscreenChoices[1];

        var p = settings.Current.Position;
        Assert.Equal(PositionMode.Fixed, p.Mode);
        Assert.False(page.IsPinToWindow);
        Assert.Equal(20, p.BottomOffsetPercent);
        Assert.Equal(60, p.MaxWidthPercent);
        Assert.Equal(FullscreenPosition.SameAsWindow, p.Fullscreen);
        Assert.False(p.ClickThrough);
    }

    [Fact]
    public void Shortcuts_EditConflictsAndRestore()
    {
        var settings = Services.Settings();
        var page = new ShortcutsPageModel(settings);
        var toggle = page.Rows.Single(r => r.Action == HotkeyAction.Toggle);

        toggle.Shortcut = "Ctrl+Shift+C";
        Assert.Equal("Ctrl+Shift+C", settings.Current.Hotkeys.Toggle);
        Assert.Equal("Already used to show or hide captions.", toggle.FindConflict("Ctrl+Alt+H"));

        page.ShowProblems([HotkeyAction.Hide]);
        Assert.NotNull(page.Rows.Single(r => r.Action == HotkeyAction.Hide).Problem);
        Assert.Null(toggle.Problem);

        page.RestoreDefaultsCommand.Execute(null);
        Assert.Equal("Ctrl+Alt+C", toggle.Shortcut);
    }

    [Fact]
    public void About_OpensTheRepository()
    {
        var opened = new List<string>();
        var page = new AboutPageModel(new UpdateStatus(new FakeChecker(), "0.1.0", opened.Add), opened.Add);

        page.ViewOnGitHubCommand.Execute(null);
        page.ReportProblemCommand.Execute(null);

        Assert.Equal([AboutPageModel.RepoUrl, AboutPageModel.RepoUrl + "/issues/new"], opened);
        Assert.Contains(page.Credits, c => c.Name == "Moonshine" && c.Detail == "Speech recognition · MIT");
        Assert.StartsWith("Version 0.1.0 · ", page.VersionLine);
    }
}

public class SourcePageTests
{
    private static AudioAppInfo App(int pid, string exe, string name, float peak = 0.3f) => new(pid, exe, name, $@"C:\{exe}", peak, null);

    private static readonly AudioAppInfo Chrome = App(100, "chrome.exe", "Google Chrome");
    private static readonly AudioAppInfo Spotify = App(200, "Spotify.exe", "Spotify");

    [Fact]
    public void Rows_FollowTheAppsLive_KeepingTheirInstances()
    {
        var state = new AppState { Apps = [Chrome, Spotify] };
        var page = new SourcePageModel(Services.Settings(), state);
        var chromeRow = page.Apps[0];

        state.Apps = [Spotify, Chrome with { PeakLevel = 0.9f }];

        Assert.Equal(["Spotify", "Google Chrome"], page.Apps.Select(a => a.Name));
        Assert.Same(chromeRow, page.Apps[1]);
        Assert.Equal(0.9, chromeRow.Level, 3);

        state.Apps = [Chrome];
        Assert.Single(page.Apps);
        Assert.Same(page.SystemAudio, page.Rows[^1]);
        Assert.False(page.SystemAudio.ShowMeter);
    }

    [Fact]
    public void Auto_ChecksTheAppBeingCaptioned()
    {
        var state = new AppState { Apps = [Chrome, Spotify], Target = CaptureTarget.For(Spotify) };
        var page = new SourcePageModel(Services.Settings(), state);

        Assert.True(page.FollowPlaying);
        Assert.False(page.Apps[0].IsChecked);
        Assert.True(page.Apps[1].IsChecked);
        Assert.False(page.SystemAudio.IsChecked);
    }

    [Fact]
    public void ChoosingAnApp_PinsIt_AndTurnsFollowingOff()
    {
        var settings = Services.Settings();
        var page = new SourcePageModel(settings, new AppState { Apps = [Chrome, Spotify] });

        page.Apps[1].IsChecked = true;

        Assert.Equal(SourceMode.Pinned, settings.Current.Source.Mode);
        Assert.Equal("Spotify.exe", settings.Current.Source.PinnedExe);
        Assert.False(page.FollowPlaying);
        Assert.True(page.Apps[1].IsChecked);
    }

    [Fact]
    public void SystemAudio_And_FollowAgain()
    {
        var settings = Services.Settings();
        var page = new SourcePageModel(settings, new AppState { Apps = [Chrome] });

        page.SystemAudio.IsChecked = true;
        Assert.Equal(SourceMode.System, settings.Current.Source.Mode);
        Assert.True(page.SystemAudio.IsChecked);

        page.FollowPlaying = true;
        Assert.Equal(SourceMode.Auto, settings.Current.Source.Mode);
    }

    [Fact]
    public void FollowOff_PinsTheCurrentTarget()
    {
        var settings = Services.Settings();
        var page = new SourcePageModel(settings, new AppState { Apps = [Spotify, Chrome], Target = CaptureTarget.For(Chrome) });

        page.FollowPlaying = false;

        Assert.Equal("chrome.exe", settings.Current.Source.PinnedExe);
    }

    [Fact]
    public void WithoutPerAppCapture_OnlySystemAudio()
    {
        var page = new SourcePageModel(Services.Settings(), new AppState { Apps = [Chrome], SupportsPerProcess = false });

        Assert.Empty(page.Apps);
        Assert.True(page.SystemAudio.IsChecked);
        Assert.StartsWith("Per-app capture needs Windows 11", page.Footer);
    }

    [Fact]
    public void Subtitles_ComeFromMedia()
    {
        var now = DateTimeOffset.Now;
        MediaState Session(string title, PlaybackStatus status) => new("Chrome", title, "", status, TimeSpan.Zero, TimeSpan.Zero, now, true);

        Assert.Equal(("Inside a jet engine", true), SourcePageModel.Describe(Chrome, [Chrome], [Session("Inside a jet engine", PlaybackStatus.Playing)]));
        Assert.Equal(("Paused", false), SourcePageModel.Describe(Chrome, [Chrome], [Session("Inside a jet engine", PlaybackStatus.Paused)]));
        Assert.Equal(("Music", false), SourcePageModel.Describe(Spotify, [Spotify], []));
        Assert.Equal((null, false), SourcePageModel.Describe(Chrome, [Chrome], []));
    }

    [Fact]
    public void IsShowing_RaisesThePollingRate()
    {
        var state = new AppState();
        var page = new SourcePageModel(Services.Settings(), state) { IsShowing = true };
        Assert.True(state.ShowingLevels);
    }
}

public class SpeechPageTests
{
    private static SpeechPageModel Page(SettingsService settings, FakeModels models, BenchResult? bench = null) =>
        new(settings, models, (_, _) => Task.FromResult(bench ?? new BenchResult("tiny", 34, 0.2)), [new GraphicsAdapter("4D4F4351:37314430", "Adreno", 0)]);

    [Fact]
    public void Rows_ShowInUseInstalledAndAvailable()
    {
        var page = Page(Services.Settings(), new FakeModels());

        Assert.Equal([ModelRowState.InUse, ModelRowState.Available, ModelRowState.Available], page.Rows.Select(r => r.State));
        Assert.True(page.Rows[0].IsRecommended);
        Assert.Equal("34M parameters", page.Rows[0].Parameters);
        Assert.DoesNotContain(page.Rows, r => r.Description.Contains("graphics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Download_ThenUse_SwitchesTheModel()
    {
        var settings = Services.Settings();
        var page = Page(settings, new FakeModels());
        var small = page.Rows[1];

        await page.DownloadAsync(small);
        Assert.Equal(ModelRowState.Installed, small.State);

        page.Use(small);
        Assert.Equal(FakeModels.Small, settings.Current.Engine.Model);
        Assert.Equal(ModelRowState.InUse, small.State);
        Assert.Equal(ModelRowState.Installed, page.Rows[0].State);
    }

    [Fact]
    public async Task FailedDownload_ShowsAMessage()
    {
        var page = Page(Services.Settings(), new FakeModels { DownloadError = new HttpRequestException() });

        await page.DownloadAsync(page.Rows[2]);

        Assert.Equal(ModelRowState.Available, page.Rows[2].State);
        Assert.Contains("didn't download", page.Message);
    }

    [Fact]
    public void Use_IgnoresAModelNotInstalled()
    {
        var settings = Services.Settings();
        var page = Page(settings, new FakeModels());

        page.Use(page.Rows[2]);

        Assert.Equal("moonshine-tiny-streaming", settings.Current.Engine.Model);
    }

    [Fact]
    public async Task SpeedTest_MovesTheRecommendation()
    {
        var page = Page(Services.Settings(), new FakeModels(), new BenchResult("tiny", 34, 0.2));

        await page.SpeedTestCommand.RunAsync();

        Assert.True(page.Rows[1].IsRecommended);
        Assert.False(page.Rows[0].IsRecommended);
        Assert.Contains("took 2.0 s to caption 10 s", page.TestDescription);
        Assert.Contains("Best for this PC: Moonshine Small Streaming", page.TestDescription);
    }

    [Fact]
    public void GraphicsCard_IsListed_ButNotUsable()
    {
        var settings = Services.Settings();
        var page = Page(settings, new FakeModels());

        Assert.False(page.CanUseGraphicsCard);
        Assert.Equal("Adreno", page.Adapter.Label);
        page.RunOnIndex = 2;
        Assert.Equal(EngineDevice.Cpu, settings.Current.Engine.Device);
    }
}

public class TranscriptsPageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "captly-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Options_AndFolderPicker()
    {
        var settings = Services.Settings();
        var shell = new FakeShell { FolderToPick = _folder };
        var page = new TranscriptsPageModel(settings, shell) { Txt = false, NameAfterVideo = false };

        page.ChangeFolderCommand.Execute(null);

        Assert.False(settings.Current.Transcripts.Txt);
        Assert.False(settings.Current.Transcripts.NameAfterVideo);
        Assert.Equal(_folder, settings.Current.Transcripts.Folder);
        Assert.Equal(_folder, page.Folder);
    }

    [Fact]
    public void Recent_ListsTranscripts_WithDetail()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "2026-11-03 - Inside a jet engine.txt"), "Inside a jet engine\nGoogle Chrome · 3 November 2026\n\nThe fan.\n");
        File.WriteAllText(Path.Combine(_folder, "2026-11-03 - Inside a jet engine.srt"), "1\n00:00:01,000 --> 00:00:02,000\nThe fan.\n");
        var shell = new FakeShell();
        var page = new TranscriptsPageModel(Services.Settings(initial: new AppSettings { Transcripts = new TranscriptSettings { Folder = _folder } }), shell, () => DateTime.Today);

        page.Refresh();

        var recent = Assert.Single(page.Recent);
        Assert.Equal("Inside a jet engine", recent.Title);
        Assert.Equal("Today · Google Chrome · TXT + SRT", recent.Detail);
        recent.OpenCommand.Execute(null);
        Assert.EndsWith(".txt", Assert.Single(shell.Opened));
    }

    [Theory]
    [InlineData(@"C:\Users\a\Documents\Captly", "Documents › Captly")]
    [InlineData(@"C:\Users\a\Documents\Notes\Captly", "Documents › Notes › Captly")]
    [InlineData(@"D:\Transcripts", @"D:\Transcripts")]
    public void FolderDisplay_ShortensDocuments(string folder, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, TranscriptsPageModel.DisplayFolder(folder, @"C:\Users\a\Documents"));
    }

    [Fact]
    public void Detail_SaysYesterdayAndOlderDates()
    {
        var today = new DateTime(2026, 11, 3);
        Assert.StartsWith("Yesterday", TranscriptsPageModel.Detail(new TranscriptEntry("A", null, today.AddDays(-1), "a.txt", null), today));
        Assert.Equal("1 Nov 2026 · SRT", TranscriptsPageModel.Detail(new TranscriptEntry("A", null, today.AddDays(-2), null, "a.srt"), today));
    }
}

public class PopoverModelTests
{
    private readonly AppState _state = new() { IsOn = true, Status = CaptionStatus.Captioning, Target = new CaptureTarget(100, "chrome.exe", "Google Chrome"), ModelName = "Tiny model" };
    private readonly SettingsService _settings = Services.Settings();
    private readonly FakeShell _shell = new();
    private readonly PopoverModel _popover;

    public PopoverModelTests() => _popover = new PopoverModel(_state, _settings, new FakeCaptioning(_state), _shell);

    [Fact]
    public void Status_AndNowPlaying()
    {
        _state.Media = new MediaState("Chrome", "Inside a jet engine", "Engines Explained", PlaybackStatus.Playing, TimeSpan.FromMinutes(12.5), TimeSpan.FromMinutes(25), DateTimeOffset.Now, true);
        _state.Position = TimeSpan.FromMinutes(12.5);

        Assert.Equal("Captioning Google Chrome", _popover.StatusText);
        Assert.True(_popover.IsLive);
        Assert.Equal("Engines Explained · Google Chrome", _popover.MediaDetail);
        Assert.Equal(0.5, _popover.Progress, 3);
        Assert.Equal("12:30", _popover.PositionText);
        Assert.Equal("25:00", _popover.DurationText);
    }

    [Fact]
    public void Toggles_ReachThePipelineAndSettings()
    {
        _popover.IsOn = false;
        _popover.IsHidden = true;
        _popover.PinToVideo = false;
        _popover.SaveTranscript = false;

        Assert.False(_state.IsOn);
        Assert.True(_state.IsHidden);
        Assert.Equal(PositionMode.Fixed, _settings.Current.Position.Mode);
        Assert.Equal("Off", _popover.PinText);
        Assert.Equal("Off", _popover.SaveText);
    }

    [Fact]
    public void Tiles_ShowWhatIsUsed()
    {
        Assert.Equal("Processor", _popover.DeviceTitle);
        Assert.Equal("Tiny model", _popover.ModelText);
        Assert.Equal("TXT + SRT", _popover.SaveText);
        Assert.Equal("Ctrl + Alt + H", _popover.HideHint);
        Assert.Equal("Following what's playing", _popover.SourceText);

        _settings.Update(s => s with { Source = s.Source with { Mode = SourceMode.System } });
        Assert.Equal("All system audio", _popover.SourceText);
    }

    [Fact]
    public void Commands_OpenTheRightPlaces()
    {
        _popover.OpenSourceCommand.Execute(null);
        _popover.OpenSettingsCommand.Execute(null);
        _popover.QuitCommand.Execute(null);

        Assert.Equal(["Source", null], _shell.SettingsPages);
        Assert.True(_shell.QuitCalled);
    }

    [Fact]
    public void NoCaptionYet_ShowsAHint()
    {
        Assert.False(_popover.HasCaption);
        _state.LatestCaption = "The fan pulls in air.";
        Assert.Equal("The fan pulls in air.", _popover.LatestCaption);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(761, "12:41")]
    [InlineData(3725, "1:02:05")]
    public void Clock_FormatsMinutesAndHours(int seconds, string expected) =>
        Assert.Equal(expected, PopoverModel.Clock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(CaptionStatus.Off, "Captions are off")]
    [InlineData(CaptionStatus.Waiting, "Waiting for a video")]
    [InlineData(CaptionStatus.Paused, "Paused")]
    [InlineData(CaptionStatus.NoModel, "Finish setting up Captly")]
    public void StatusText_ForEachState(CaptionStatus status, string expected)
    {
        _state.Status = status;
        Assert.Equal(expected, _popover.StatusText);
    }
}

internal sealed class FakeChecker(UpdateCheck? result = null, Exception? error = null) : IUpdateChecker
{
    public Task<UpdateCheck> CheckAsync(CancellationToken ct) =>
        error != null ? Task.FromException<UpdateCheck>(error) : Task.FromResult(result ?? new UpdateCheck(UpdateOutcome.UpToDate));
}

public class UpdateStatusTests
{
    [Fact]
    public async Task UpToDate_Available_AndFailed()
    {
        var opened = new List<string>();

        var current = new UpdateStatus(new FakeChecker(), "0.1.0", opened.Add);
        await current.CheckAsync();
        Assert.Equal("You're up to date.", current.Text);
        Assert.False(current.OpenReleaseCommand.CanExecute(null));

        var newer = new UpdateStatus(new FakeChecker(new UpdateCheck(UpdateOutcome.Available, "0.2.0", "https://example.test/r")), "0.1.0", opened.Add);
        await newer.CheckAsync();
        Assert.Equal("Captly 0.2.0 is available.", newer.Text);
        newer.OpenReleaseCommand.Execute(null);
        Assert.Equal(["https://example.test/r"], opened);

        var offline = new UpdateStatus(new FakeChecker(error: new HttpRequestException()), "0.1.0", opened.Add);
        await offline.CheckAsync();
        Assert.Equal("Couldn't check for updates. Try again later.", offline.Text);
    }
}
