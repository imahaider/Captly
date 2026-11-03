using Captly.Core.Audio;
using Captly.Core.Media;

namespace Captly.Core.Shell;

public enum CaptionStatus { Off, Waiting, Captioning, Paused, NoModel }

/// <summary>
/// Everything the popover, tray and Source screen show (PLAN.md §5.3). The coordinator writes it
/// on the UI thread, and views bind to it.
/// </summary>
public sealed class AppState : Observable
{
    private bool _isOn;
    private bool _isHidden;
    private CaptionStatus _status;
    private CaptureTarget? _target;
    private MediaState? _media;
    private TimeSpan _position;
    private byte[]? _thumbnail;
    private string? _latestCaption;
    private IReadOnlyList<AudioAppInfo> _apps = [];
    private IReadOnlyList<MediaState> _sessions = [];
    private bool _supportsPerProcess = true;
    private bool _showingLevels;
    private string _modelName = "";
    private string? _transcriptPath;

    /// <summary>Captions are switched on: the tray toggle and Ctrl + Alt + C.</summary>
    public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }

    /// <summary>Captions keep running, and the transcript keeps growing, but nothing shows on screen.</summary>
    public bool IsHidden { get => _isHidden; set => Set(ref _isHidden, value); }

    public CaptionStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value)) Raise(nameof(StatusText));
        }
    }

    /// <summary>The app being heard. Null when nothing is.</summary>
    public CaptureTarget? Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, value)) Raise(nameof(StatusText));
        }
    }

    /// <summary>The status line of the popover and the tray tooltip.</summary>
    public string StatusText => Status switch
    {
        CaptionStatus.Off => "Captions are off",
        CaptionStatus.Captioning when Target != null => $"Captioning {Target.DisplayName}",
        CaptionStatus.Captioning => "Captioning",
        CaptionStatus.Paused => "Paused",
        CaptionStatus.NoModel => "Finish setting up Captly",
        _ => "Waiting for a video",
    };

    /// <summary>The captured app's media session, if it has one.</summary>
    public MediaState? Media { get => _media; set => Set(ref _media, value); }

    /// <summary>The media position, carried forward from the last report.</summary>
    public TimeSpan Position { get => _position; set => Set(ref _position, value); }

    /// <summary>The media's artwork as image bytes.</summary>
    public byte[]? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

    public string? LatestCaption { get => _latestCaption; set => Set(ref _latestCaption, value); }

    /// <summary>Apps making sound, loudest first.</summary>
    public IReadOnlyList<AudioAppInfo> Apps { get => _apps; set => Set(ref _apps, value); }

    public IReadOnlyList<MediaState> Sessions { get => _sessions; set => Set(ref _sessions, value); }

    public bool SupportsPerProcess { get => _supportsPerProcess; set => Set(ref _supportsPerProcess, value); }

    /// <summary>Set while the Source screen shows level meters, so apps are polled faster.</summary>
    public bool ShowingLevels { get => _showingLevels; set => Set(ref _showingLevels, value); }

    /// <summary>Such as "Tiny model".</summary>
    public string ModelName { get => _modelName; set => Set(ref _modelName, value); }

    /// <summary>The transcript being written, for Ctrl + Alt + T.</summary>
    public string? TranscriptPath { get => _transcriptPath; set => Set(ref _transcriptPath, value); }
}

/// <summary>What the popover, tray and hotkeys ask of the caption pipeline.</summary>
public interface ICaptioning
{
    void SetOn(bool on);

    void SetHidden(bool hidden);

    /// <summary>Pins the next app playing sound, after the one being captioned.</summary>
    void NextSource();
}

/// <summary>Things only the app shell can do.</summary>
public interface IShellServices
{
    /// <summary>Opens Settings, optionally at a page such as "Source".</summary>
    void OpenSettings(string? page = null);

    /// <summary>Opens first-run setup again, to fetch a speech model.</summary>
    void OpenSetup();

    /// <summary>Opens a file, a folder or a web address with its usual app.</summary>
    void Open(string target);

    /// <summary>Asks for a folder. Null if cancelled.</summary>
    string? PickFolder(string current);

    void Quit();
}

/// <summary>Turns the "Start Captly when Windows starts" setting into the real registration.</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void Set(bool enabled);
}
