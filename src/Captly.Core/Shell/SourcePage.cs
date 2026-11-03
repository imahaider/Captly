using System.Collections.ObjectModel;
using Captly.Core.Audio;
using Captly.Core.Media;
using Captly.Core.Settings;

namespace Captly.Core.Shell;

/// <summary>One app in the Source list. Checking it pins captions to that app.</summary>
public sealed class AppRowModel : Observable
{
    private readonly Action<AppRowModel> _choose;
    private string _name;
    private string? _subtitle;
    private string? _iconPath;
    private double _level;
    private bool _isNowPlaying;
    private bool _isChecked;

    public AppRowModel(int? pid, string exeName, string name, Action<AppRowModel> choose)
    {
        Pid = pid;
        ExeName = exeName;
        _name = name;
        _choose = choose;
    }

    /// <summary>Null for "All system audio".</summary>
    public int? Pid { get; }

    public string ExeName { get; }

    public string Name { get => _name; set => Set(ref _name, value); }

    public string? Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

    /// <summary>The exe, for its icon.</summary>
    public string? IconPath { get => _iconPath; set => Set(ref _iconPath, value); }

    /// <summary>"All system audio" has no single level to show.</summary>
    public bool ShowMeter => Pid != null;

    /// <summary>Peak level from 0 to 1.</summary>
    public double Level { get => _level; set => Set(ref _level, value); }

    public bool IsNowPlaying { get => _isNowPlaying; set => Set(ref _isNowPlaying, value); }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (value && !_isChecked) _choose(this);
        }
    }

    internal void ShowChecked(bool isChecked) => Set(ref _isChecked, isChecked, nameof(IsChecked));
}

/// <summary>
/// The Source screen (PLAN.md §6.3 #2). The list follows <see cref="AppState.Apps"/> live, keeping
/// rows in place so meters move smoothly. Picking an app pins it, picking "All system audio"
/// captions everything, and "Follow what's playing" goes back to Auto.
/// </summary>
public sealed class SourcePageModel : SettingsPageModel
{
    private readonly AppState _state;

    public SourcePageModel(SettingsService settings, AppState state) : base(settings)
    {
        _state = state;
        SystemAudio = new AppRowModel(null, "", "All system audio", _ => Change(s => s with { Source = s.Source with { Mode = SourceMode.System } }))
        {
            Subtitle = "Everything your speakers play, including notifications",
        };
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.Apps) or nameof(AppState.Target) or nameof(AppState.Sessions) or nameof(AppState.SupportsPerProcess)) Refresh();
        };
        settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The rows of the card: apps, loudest first, then "All system audio".</summary>
    public ObservableCollection<AppRowModel> Rows { get; } = [];

    public IReadOnlyList<AppRowModel> Apps => Rows.Where(r => r.Pid != null).ToList();

    public AppRowModel SystemAudio { get; }

    public bool SupportsPerProcess => _state.SupportsPerProcess;

    /// <summary>On means Auto. Off pins whichever app is being captioned now.</summary>
    public bool FollowPlaying
    {
        get => Current.Source.Mode == SourceMode.Auto;
        set => Change(s => s with
        {
            Source = value
                ? s.Source with { Mode = SourceMode.Auto }
                : s.Source with { Mode = SourceMode.Pinned, PinnedExe = _state.Target is { IsSystem: false } target ? target.ExeName : _state.Apps.FirstOrDefault()?.ExeName },
        });
    }

    public IReadOnlyList<Choice<int>> SwitchDelayChoices { get; } = [new("1 second", 1000), new("2 seconds", 2000), new("5 seconds", 5000)];

    public Choice<int> SwitchDelay
    {
        get => SwitchDelayChoices.FirstOrDefault(c => c.Value == Current.Source.SwitchDelayMs) ?? SwitchDelayChoices[1];
        set => Change(s => s with { Source = s.Source with { SwitchDelayMs = value.Value } });
    }

    public string Footer => SupportsPerProcess
        ? "Per-app capture is on. Only the selected app is heard — music and notifications from other apps are ignored."
        : "Per-app capture needs Windows 11. Captly is listening to all system audio.";

    /// <summary>The view sets this while the screen is open, so meters update ten times a second.</summary>
    public bool IsShowing { set => _state.ShowingLevels = value; }

    /// <summary>"Paused", "Music", the playing title, or the window title that tells two copies apart.</summary>
    public static (string? Subtitle, bool IsNowPlaying) Describe(AudioAppInfo app, IReadOnlyList<AudioAppInfo> apps, IReadOnlyList<MediaState> sessions)
    {
        var media = sessions.FirstOrDefault(s => MediaMatching.AppFor(s, apps)?.RootPid == app.RootPid);
        if (media?.Status == PlaybackStatus.Playing && media.Title.Length > 0) return (media.Title, true);
        if (media?.Status is PlaybackStatus.Paused) return ("Paused", false);
        if (MediaMatching.IsMusicApp(app.ExeName)) return ("Music", false);
        return (app.Subtitle, false);
    }

    private void Refresh()
    {
        var apps = _state.SupportsPerProcess ? _state.Apps : [];
        if (!Rows.Contains(SystemAudio)) Rows.Add(SystemAudio);
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Pid is { } pid && apps.All(a => a.RootPid != pid)) Rows.RemoveAt(i);
        }

        // Rows are moved, not rebuilt, so meters and focus stay put. System audio stays last.
        for (var i = 0; i < apps.Count; i++)
        {
            var app = apps[i];
            var row = Rows.FirstOrDefault(r => r.Pid == app.RootPid);
            if (row == null)
            {
                row = new AppRowModel(app.RootPid, app.ExeName, app.DisplayName, Choose) { IconPath = app.IconPath };
                Rows.Insert(i, row);
            }
            else if (Rows.IndexOf(row) != i)
            {
                Rows.Move(Rows.IndexOf(row), i);
            }

            var (subtitle, playing) = Describe(app, apps, _state.Sessions);
            row.Name = app.DisplayName;
            row.Subtitle = subtitle;
            row.IsNowPlaying = playing;
            row.Level = app.PeakLevel;
            row.ShowChecked(IsSelected(row));
        }

        var source = Current.Source;
        SystemAudio.ShowChecked(!_state.SupportsPerProcess || source.Mode == SourceMode.System || source.Mode == SourceMode.Auto && _state.Target is { IsSystem: true });
        Raise(nameof(SupportsPerProcess));
        Raise(nameof(Footer));
    }

    private bool IsSelected(AppRowModel row)
    {
        var source = Current.Source;
        return source.Mode switch
        {
            SourceMode.Pinned => SourceSelector.SameExe(row.ExeName, source.PinnedExe),
            SourceMode.Auto => _state.Target?.RootPid == row.Pid,
            _ => false,
        };
    }

    private void Choose(AppRowModel row) =>
        Change(s => s with { Source = s.Source with { Mode = SourceMode.Pinned, PinnedExe = row.ExeName } });
}
