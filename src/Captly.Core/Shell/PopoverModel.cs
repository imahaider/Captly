using System.ComponentModel;
using Captly.Core.Media;
using Captly.Core.Settings;
using Captly.Core.Transcripts;

namespace Captly.Core.Shell;

/// <summary>The tray popover (PLAN.md §6.3 #9), bound to <see cref="AppState"/> and the settings.</summary>
public sealed class PopoverModel : Observable
{
    private readonly AppState _state;
    private readonly SettingsService _settings;
    private readonly ICaptioning _captioning;

    public PopoverModel(AppState state, SettingsService settings, ICaptioning captioning, IShellServices shell)
    {
        _state = state;
        _settings = settings;
        _captioning = captioning;
        state.PropertyChanged += OnStateChanged;
        settings.Changed += (_, _) => RaiseAll();

        OpenSourceCommand = new Command(() => shell.OpenSettings("Source"));
        OpenSpeechCommand = new Command(() => shell.OpenSettings("Speech Model"));
        OpenSettingsCommand = new Command(() => shell.OpenSettings());
        OpenTranscriptsCommand = new Command(() =>
        {
            var folder = TranscriptNames.ResolveFolder(_settings.Current.Transcripts.Folder);
            Directory.CreateDirectory(folder);
            shell.Open(folder);
        });
        QuitCommand = new Command(shell.Quit);
        FinishSetupCommand = new Command(shell.OpenSetup);
    }

    private AppSettings Current => _settings.Current;

    public bool IsOn { get => _state.IsOn; set => _captioning.SetOn(value); }

    public string StatusText => _state.StatusText;

    /// <summary>Setup was skipped and there is no model, so captions cannot start.</summary>
    public bool NeedsSetup => _state.Status == CaptionStatus.NoModel;

    public bool HasMedia => _state.Media != null;

    public string Title => _state.Media?.Title is { Length: > 0 } title ? title : "Untitled";

    /// <summary>Such as "Channel name · Google Chrome".</summary>
    public string MediaDetail => string.Join(" · ", new[] { _state.Media?.Artist, _state.Target?.DisplayName }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public bool IsLive => _state.Status == CaptionStatus.Captioning && _state.Media?.Status == PlaybackStatus.Playing;

    public bool HasTimeline => _state.Media is { HasTimeline: true };

    public double Progress => _state.Media is { HasTimeline: true } media ? Math.Clamp(_state.Position / media.Duration, 0, 1) : 0;

    public string PositionText => Clock(_state.Position);

    public string DurationText => Clock(_state.Media?.Duration ?? TimeSpan.Zero);

    public byte[]? Thumbnail => _state.Thumbnail;

    public bool HasCaption => !string.IsNullOrWhiteSpace(_state.LatestCaption);

    public string LatestCaption => HasCaption ? _state.LatestCaption! : "Captions will appear here when someone speaks.";

    public bool PinToVideo
    {
        get => Current.Position.Mode == PositionMode.PinToWindow;
        set => _settings.Update(s => s with { Position = s.Position with { Mode = value ? PositionMode.PinToWindow : PositionMode.Fixed } });
    }

    public string PinText => PinToVideo ? "On" : "Off";

    public bool SaveTranscript
    {
        get => Current.Transcripts.Enabled;
        set => _settings.Update(s => s with { Transcripts = s.Transcripts with { Enabled = value } });
    }

    public string SaveText
    {
        get
        {
            var t = Current.Transcripts;
            if (!t.Enabled || !t.Txt && !t.Srt) return "Off";
            return t.Txt && t.Srt ? "TXT + SRT" : t.Txt ? "TXT" : "SRT";
        }
    }

    public bool IsHidden { get => _state.IsHidden; set => _captioning.SetHidden(value); }

    public string HideHint => Hotkeys.Display(Current.Hotkeys.Hide);

    /// <summary>D22: names the graphics card only when one is in use, which is never yet.</summary>
    public string DeviceTitle => "Processor";

    public string ModelText => _state.ModelName;

    public string SourceText => Current.Source.Mode switch
    {
        SourceMode.Auto => "Following what's playing",
        SourceMode.System => "All system audio",
        _ => _state.Apps.FirstOrDefault(a => Audio.SourceSelector.SameExe(a.ExeName, Current.Source.PinnedExe))?.DisplayName
            ?? Current.Source.PinnedExe ?? "Following what's playing",
    };

    public Command OpenSourceCommand { get; }

    public Command OpenSpeechCommand { get; }

    public Command OpenSettingsCommand { get; }

    public Command OpenTranscriptsCommand { get; }

    public Command QuitCommand { get; }

    public Command FinishSetupCommand { get; }

    /// <summary>"12:41", or "1:02:05" past an hour.</summary>
    public static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";

    // Apps and levels change ten times a second while Settings shows meters; the popover shows neither.
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.Sessions) or nameof(AppState.ShowingLevels)) return;
        if (e.PropertyName == nameof(AppState.Apps) && Current.Source.Mode != SourceMode.Pinned) return;
        RaiseAll();
    }
}
