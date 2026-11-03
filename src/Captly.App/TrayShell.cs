using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;
using Captly.Core.Transcripts;
using Captly.Speech;
using Captly.UI;
using Captly.UI.Popover;
using Captly.UI.Settings;
using Captly.UI.Setup;
using Captly.UI.Shell;
using Captly.Windowing;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Captly.App;

/// <summary>
/// The app as the user meets it (PLAN.md Phase 8): tray icon, popover, Settings window and global
/// shortcuts. It also applies the settings that belong to the shell: theme, shortcuts and start
/// with Windows.
/// </summary>
internal sealed class TrayShell : IShellServices, IDisposable
{
    private static readonly TimeSpan PopoverReopenGuard = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(15);

    private readonly Application _app;
    private readonly SettingsService _settings;
    private readonly AppState _state;
    private readonly Coordinator _coordinator;
    private readonly IModelManager _models;
    private readonly IStartupRegistration _startup;
    private readonly UpdateStatus _updates;
    private readonly ILogger _logger;
    private GlobalHotkeys? _hotkeys;
    private TrayIcon? _tray;
    private PopoverWindow? _popover;
    private SettingsModel? _settingsModel;
    private SettingsWindow? _settingsWindow;
    private SetupWindow? _setupWindow;

    public TrayShell(Application app, SettingsService settings, AppState state, Coordinator coordinator, IModelManager models,
        IStartupRegistration startup, IUpdateChecker updateChecker, string version, ILogger<TrayShell> logger)
    {
        _app = app;
        _settings = settings;
        _state = state;
        _coordinator = coordinator;
        _models = models;
        _startup = startup;
        _logger = logger;
        _updates = new UpdateStatus(updateChecker, version, Open);
    }

    public void Start()
    {
        _hotkeys = new GlobalHotkeys();
        _hotkeys.Pressed += OnHotkey;
        ApplyHotkeys();

        _popover = new PopoverWindow(new PopoverModel(_state, _settings, _coordinator, this));
        _tray = new TrayIcon(BuildMenu, _hotkeys.Handle);
        _tray.Clicked += TogglePopover;
        _state.PropertyChanged += OnStateChanged;
        _settings.Changed += OnSettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        UpdateTray();

        if (_startup.IsEnabled != _settings.Current.General.StartWithWindows) _startup.Set(_settings.Current.General.StartWithWindows);
        if (_settings.Current.General.CheckUpdates) _ = CheckForUpdatesLaterAsync();
        _logger.LogInformation("Tray ready");
    }

    public void OpenSettings(string? page = null)
    {
        _popover?.Hide();
        if (_settingsWindow == null)
        {
            _settingsModel ??= BuildSettingsModel();
            _settingsWindow = new SettingsWindow(_settingsModel);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (page != null) _settingsWindow.View.ShowPage(page);
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>First-run setup. Captions start when it is finished.</summary>
    public void OpenSetup()
    {
        _popover?.Hide();
        if (_setupWindow == null)
        {
            var model = new SetupModel(_settings, _models, (m, ct) => SpeedTest.RunAsync(m, _models.GetModelPath(m.Id), ct));
            model.Finished += OnSetupFinished;
            _setupWindow = new SetupWindow(model);
            _setupWindow.Closed += (_, _) => _setupWindow = null;
        }
        _setupWindow.Show();
        _setupWindow.Activate();
    }

    public void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException)
        {
            _logger.LogWarning(e, "Could not open {Target}", target);
        }
    }

    public string? PickFolder(string current)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(current) ? current : "", Title = "Choose where transcripts are saved" };
        return dialog.ShowDialog(_settingsWindow) == true ? dialog.FolderName : null;
    }

    public void Quit() => _app.Shutdown();

    public void Dispose()
    {
        _state.PropertyChanged -= OnStateChanged;
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _settingsWindow?.Close();
        _setupWindow?.Close();
        _popover?.Close();
        _tray?.Dispose();
        _hotkeys?.Dispose();
    }

    private SettingsModel BuildSettingsModel()
    {
        var shortcuts = new ShortcutsPageModel(_settings);
        shortcuts.ShowProblems(_lastRefused);
        return new SettingsModel(
            new GeneralPageModel(_settings, _updates),
            new SourcePageModel(_settings, _state),
            new AppearancePageModel(_settings),
            new PositionPageModel(_settings),
            new SpeechPageModel(_settings, _models, (model, ct) => SpeedTest.RunAsync(model, _models.GetModelPath(model.Id), ct), GraphicsAdapters.List()),
            new TranscriptsPageModel(_settings, this),
            shortcuts,
            new AboutPageModel(_updates, Open));
    }

    private IReadOnlyList<HotkeyAction> _lastRefused = [];

    // Skipped with no model: captions cannot run, so the tray says so once and the popover offers setup.
    private void OnSetupFinished(bool completed)
    {
        _logger.LogInformation("Setup {Outcome}", completed ? "finished" : "skipped");
        if (completed)
        {
            _coordinator.SetOn(true);
            return;
        }
        if (_models.Available.Any(m => m.IsInstalled))
        {
            _coordinator.SetOn(_settings.Current.General.AutoCaption);
            return;
        }
        _coordinator.SetOn(true);
        _tray?.Notify("Finish setting up Captly", "Captions need a speech model. Click the Captly icon to finish setting up.");
    }

    private void ApplyHotkeys()
    {
        _lastRefused = _hotkeys!.Apply(_settings.Current.Hotkeys);
        foreach (var action in _lastRefused) _logger.LogWarning("Shortcut {Keys} for {Action} is taken by another app", Hotkeys.Get(_settings.Current.Hotkeys, action), action);
        _settingsModel?.Shortcuts.ShowProblems(_lastRefused);
    }

    private void OnHotkey(HotkeyAction action)
    {
        _logger.LogInformation("Shortcut: {Action}", action);
        switch (action)
        {
            case HotkeyAction.Toggle:
                _coordinator.SetOn(!_state.IsOn);
                break;
            case HotkeyAction.Hide:
                _coordinator.SetHidden(!_state.IsHidden);
                break;
            case HotkeyAction.NextSource:
                _coordinator.NextSource();
                break;
            case HotkeyAction.OpenTranscript:
                var folder = TranscriptNames.ResolveFolder(_settings.Current.Transcripts.Folder);
                if (_state.TranscriptPath is { } path && File.Exists(path)) Open(path);
                else
                {
                    Directory.CreateDirectory(folder);
                    Open(folder);
                }
                break;
            case HotkeyAction.OpenSettings:
                OpenSettings();
                break;
        }
    }

    private void OnSettingsChanged(AppSettings old, AppSettings now)
    {
        if (old.General.Theme != now.General.Theme) ThemeManager.Apply(now.General.Theme);
        if (old.Hotkeys != now.Hotkeys) ApplyHotkeys();
        if (old.General.StartWithWindows != now.General.StartWithWindows) _startup.Set(now.General.StartWithWindows);
        if (!old.General.CheckUpdates && now.General.CheckUpdates) _ = _updates.CheckAsync();
    }

    // Raised on a system thread. Capture and shortcuts are set up again on the UI thread.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _app.Dispatcher.BeginInvoke(() =>
        {
            _logger.LogInformation("Resumed from sleep");
            _coordinator.Restart();
            ApplyHotkeys();
        });
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.IsOn) or nameof(AppState.Status) or nameof(AppState.Media) or nameof(AppState.Target)) UpdateTray();
    }

    private void UpdateTray()
    {
        if (_tray == null) return;
        _tray.State = !_state.IsOn ? TrayState.Off : _state.Status == CaptionStatus.Captioning ? TrayState.On : TrayState.Waiting;
        var detail = _state.Media?.Title is { Length: > 0 } title ? title : _state.StatusText;
        _tray.ToolTip = $"Captly — {detail}";
    }

    private void TogglePopover()
    {
        if (_popover == null) return;
        if (_popover.IsVisible)
        {
            _popover.Hide();
            return;
        }
        // The click that closed it, by taking focus away, must not open it again.
        if (DateTime.UtcNow - _popover.LastHidden < PopoverReopenGuard) return;

        GetCursorPos(out var cursor);
        var monitor = Monitors.FromPoint(new Core.Windowing.PixelPoint(cursor.X, cursor.Y));
        _popover.ShowAt(monitor.WorkArea, monitor.Scale);
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu() =>
    [
        _state.Status == CaptionStatus.NoModel
            ? new("Finish Setup…", OpenSetup)
            : new(_state.IsOn ? "Turn Captions Off" : "Turn Captions On", () => _coordinator.SetOn(!_state.IsOn)),
        new("Hide Captions", () => _coordinator.SetHidden(!_state.IsHidden), _state.IsHidden),
        new(null),
        new("Open Transcripts Folder", () => OnHotkey(HotkeyAction.OpenTranscript)),
        new("Settings…", () => OpenSettings()),
        new(null),
        new("Quit Captly", Quit),
    ];

    private async Task CheckForUpdatesLaterAsync()
    {
        await Task.Delay(FirstUpdateCheck);
        await _updates.CheckAsync();
        _logger.LogInformation("Update check: {Result}", _updates.Text);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool GetCursorPos(out Point point);
}
