using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using Captly.Audio;
using Captly.Core.Audio;
using Captly.Core.Captions;
using Captly.Core.Media;
using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;
using Captly.Core.Transcripts;
using Captly.Core.Windowing;
using Captly.Speech;
using Captly.UI.Overlay;
using Captly.Windowing;
using Microsoft.Extensions.Logging;

namespace Captly.App;

/// <summary>
/// Joins the store to the overlay on the UI thread. Runs the timer only while text is showing,
/// so that an idle app does no work.
/// </summary>
internal sealed class CaptionView
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    private readonly OverlayWindow _overlay;
    private readonly DispatcherTimer _timer;
    private CaptionStore _store = null!;

    public CaptionView(AppSettings settings, OverlayWindow overlay)
    {
        _overlay = overlay;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => _store.Tick();
        Apply(settings);
    }

    public int Updates { get; private set; }

    public OverlayWindow Overlay => _overlay;

    public void Show(CaptionEvent caption) => _store.Apply(caption);

    public void Clear() => _store.Clear();

    /// <summary>Applies new Appearance and Position settings. The store starts afresh with the new line rules.</summary>
    public void Apply(AppSettings settings)
    {
        _overlay.Apply(settings.Appearance, settings.Position);
        if (_store != null) _store.Changed -= OnChanged;
        _store = new CaptionStore(CaptionOptions.From(settings.Appearance));
        _store.Changed += OnChanged;
        OnChanged();
    }

    private void OnChanged()
    {
        Updates++;
        _overlay.ShowLines(_store.Lines);
        _timer.IsEnabled = _store.HasText;
    }
}

/// <summary>
/// Keeps the overlay on the video window, or on the fixed spot, as the Position settings say.
/// Without a window to pin to, captions sit at the bottom of the main screen.
/// </summary>
internal sealed class CaptionPinner : IDisposable
{
    private readonly OverlayWindow _overlay;
    private readonly IWindowTracker? _windows;
    private readonly Action<PixelPoint>? _saveFixed;
    private AppSettings _settings;
    private (int? Pid, string? Title) _following;

    /// <param name="windows">Null when there is no app to follow, such as a WAV.</param>
    /// <param name="saveFixed">Stores the fixed spot after a Ctrl-drag.</param>
    public CaptionPinner(OverlayWindow overlay, IWindowTracker? windows, AppSettings settings, Action<PixelPoint>? saveFixed)
    {
        _overlay = overlay;
        _windows = windows;
        _settings = settings;
        _saveFixed = saveFixed;

        _overlay.Moved += OnMoved;
        if (_windows != null)
        {
            _windows.PlacementChanged += OnPlacementChanged;
            _windows.ForegroundChanged += _overlay.BringToTop;
        }
        Update();
    }

    private bool IsFixed => _settings.Position.Mode == PositionMode.Fixed;

    private PixelPoint? FixedSpot => _settings.Position.Fixed is { } spot ? new PixelPoint((int)spot.X, (int)spot.Y) : null;

    /// <summary>Follows this app's window, using the media title to pick among several.</summary>
    public void Follow(int? rootPid, string? title)
    {
        _following = (rootPid, title);
        _windows?.Track(IsFixed ? null : rootPid, title);
    }

    /// <summary>Applies new settings, such as switching between the video window and the fixed spot.</summary>
    public void Apply(AppSettings settings)
    {
        _settings = settings;
        Follow(_following.Pid, _following.Title);
        Update();
    }

    public void Dispose()
    {
        _overlay.Moved -= OnMoved;
        if (_windows == null) return;
        _windows.PlacementChanged -= OnPlacementChanged;
        _windows.ForegroundChanged -= _overlay.BringToTop;
        _windows.Dispose();
    }

    private void OnPlacementChanged(WindowPlacement? placement) => Update();

    private void Update()
    {
        var position = _settings.Position;
        var appearance = _settings.Appearance;
        _overlay.Layout = IsFixed && FixedSpot is { } spot
            ? CaptionPlacement.AtPoint(spot, Monitors.FromPoint(spot), position, appearance)
            : !IsFixed && _windows?.Current is { } window
                ? CaptionPlacement.OnWindow(window, position, appearance)
                : CaptionPlacement.OnScreen(Monitors.Primary(), position, appearance);
    }

    private void OnMoved(PixelPoint spot) => _saveFixed?.Invoke(spot);
}

/// <summary>
/// The caption pipeline from audio to overlay and transcript, and the <see cref="AppState"/> the
/// shell shows (PLAN.md §5.2's Coordinator). Settings apply as they change. Turning captions off
/// stops capture and the worker, but keeps the model loaded so they come back at once. With a WAV
/// it plays that file silently instead of following apps.
/// </summary>
internal sealed class Coordinator : ICaptioning, IDisposable
{
    private static readonly TimeSpan MediaInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PauseDelay = TimeSpan.FromSeconds(1);

    private readonly IAudioSourceFactory _sources;
    private readonly IMediaWatcher _media;
    private readonly SettingsService _settings;
    private readonly IModelManager _models;
    private readonly AppState _state;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly string? _transcriptFolder;
    private readonly string? _wavPath;
    private readonly bool _loopWav;
    private readonly int? _pinWavTo;
    private readonly MoonshineEngine _engine = new();
    private readonly MediaTracker _tracker = new();
    private readonly HashSet<string> _unknownApps = [];
    private TranscriptWriter _transcript;
    private (string? Title, string App) _transcriptName = (null, "Captly");
    private CaptionView? _view;
    private CaptionPinner? _pinner;
    private DispatcherTimer? _mediaTimer;
    private string? _loadedModel;
    private bool _starting;

    // Alive only while captions are on.
    private IAudioSource? _source;
    private SourceController? _controller;
    private CaptionWorker? _worker;
    private AudioPreprocessor? _preprocessor;
    private DateTimeOffset? _pauseAt;

    /// <param name="transcriptFolder">Overrides the settings' folder for this run.</param>
    public Coordinator(IAudioSourceFactory sources, IMediaWatcher media, SettingsService settings, IModelManager models, AppState state,
        ILoggerFactory loggers, Dispatcher dispatcher, string? transcriptFolder = null, string? wavPath = null, bool loopWav = false, int? pinWavTo = null)
    {
        _sources = sources;
        _media = media;
        _settings = settings;
        _models = models;
        _state = state;
        _loggers = loggers;
        _logger = loggers.CreateLogger<Coordinator>();
        _dispatcher = dispatcher;
        _transcriptFolder = transcriptFolder;
        _wavPath = wavPath;
        _loopWav = loopWav;
        _pinWavTo = pinWavTo;
        _transcript = NewTranscript(settings.Current);
    }

    private AppSettings Settings => _settings.Current;

    private bool IsCapturing => _worker != null;

    public int OverlayUpdates => _view?.Updates ?? 0;

    /// <summary>Creates the overlay and starts watching media. Captions start too if <paramref name="on"/>.</summary>
    public void Start(bool on)
    {
        var overlay = new OverlayWindow();
        _view = new CaptionView(Settings, overlay);
        // A WAV has no app of its own, but can be shown on one app's window, for demonstrations.
        IWindowTracker? windows = _wavPath == null || _pinWavTo != null ? new WindowTracker(ProcessSnapshot.Take, _loggers.CreateLogger<WindowTracker>()) : null;
        _pinner = new CaptionPinner(overlay, windows, Settings, spot =>
            _settings.Update(s => s with { Position = s.Position with { Fixed = new FixedPosition(spot.X, spot.Y) } }));
        _engine.Caption += caption => _dispatcher.BeginInvoke(() => OnCaption(caption));

        _settings.Changed += OnSettingsChanged;
        _state.PropertyChanged += OnStateChanged;
        _state.SupportsPerProcess = _wavPath != null || _sources.SupportsPerProcess;
        _state.ModelName = ShortName(Settings.Engine.Model);
        _state.Status = CaptionStatus.Off;

        if (_wavPath == null)
        {
            _media.Start();
            _mediaTimer = new DispatcherTimer(MediaInterval, DispatcherPriority.Normal, (_, _) => OnMediaTick(), _dispatcher);
        }
        if (_pinWavTo != null) _pinner.Follow(_pinWavTo, null);
        if (on) SetOn(true);
    }

    public void SetOn(bool on)
    {
        if (on) _ = StartCapturingAsync();
        else StopCapturing();
    }

    /// <summary>
    /// Starts capture afresh, keeping captions on. After sleep the audio service and devices may
    /// have changed under the old capture (PLAN.md §10).
    /// </summary>
    public void Restart()
    {
        if (!IsCapturing) return;
        _logger.LogInformation("Restarting capture");
        StopCapturing();
        SetOn(true);
    }

    public void SetHidden(bool hidden)
    {
        _state.IsHidden = hidden;
        if (_view != null) _view.Overlay.IsSuppressed = hidden;
    }

    public void NextSource()
    {
        var apps = _controller?.Apps ?? [];
        if (apps.Count == 0) return;
        var current = _controller!.Target;
        var index = current?.RootPid is { } pid ? apps.ToList().FindIndex(a => a.RootPid == pid) : -1;
        var next = apps[(index + 1) % apps.Count];
        _logger.LogInformation("Next source: {App}", next.DisplayName);
        _settings.Update(s => s with { Source = s.Source with { Mode = SourceMode.Pinned, PinnedExe = next.ExeName } });
    }

    public void Dispose()
    {
        _logger.LogInformation("Caption session ended: {Updates} overlay updates, {Dropped} samples dropped, transcript {Files}",
            _view?.Updates, _preprocessor?.DroppedSamples, string.Join(", ", _transcript.CurrentFiles));
        _settings.Changed -= OnSettingsChanged;
        _state.PropertyChanged -= OnStateChanged;
        _mediaTimer?.Stop();
        StopCapturing();
        _pinner?.Dispose();
        _engine.Dispose();
        _transcript.Dispose();
        _media.Dispose();
    }

    private async Task StartCapturingAsync()
    {
        if (IsCapturing || _starting) return;
        var model = _models.Available.FirstOrDefault(m => m.Id == Settings.Engine.Model && m.IsInstalled);
        if (model == null)
        {
            _logger.LogWarning("Model {Model} is not installed, so captions cannot start", Settings.Engine.Model);
            _state.IsOn = false;
            _state.Status = CaptionStatus.NoModel;
            return;
        }

        _starting = true;
        _state.IsOn = true;
        _state.Status = CaptionStatus.Waiting;
        try
        {
            if (_loadedModel != model.Id)
            {
                var options = new EngineOptions(_models.GetModelPath(model.Id), model.Architecture);
                await Task.Run(() => _engine.Load(options));
                _loadedModel = model.Id;
                _state.ModelName = ShortName(model.Id);
                _logger.LogInformation("Loaded {Model}", model.DisplayName);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not load {Model}", model.Id);
            _state.IsOn = false;
            _state.Status = CaptionStatus.NoModel;
            return;
        }
        finally
        {
            _starting = false;
        }
        if (!_state.IsOn) return;

        _engine.Reset();
        _preprocessor = new AudioPreprocessor();
        _worker = new CaptionWorker(_preprocessor, _engine, _loggers.CreateLogger<CaptionWorker>());
        if (_wavPath != null)
        {
            _source = new FileAudioSource(_wavPath, _loopWav);
            BeginTranscript(null, Path.GetFileName(_wavPath));
            _state.Status = CaptionStatus.Captioning;
        }
        else
        {
            var switcher = new SwitchingAudioSource();
            _source = switcher;
            _controller = new SourceController(_sources, switcher, () => _settings.Current.Source, _loggers.CreateLogger<SourceController>(),
                mediaOwner: apps => MediaMatching.PickOwner(_media.Sessions, apps)?.App.RootPid)
            {
                ShowingLevels = _state.ShowingLevels,
            };
            _controller.TargetChanged += target => _dispatcher.BeginInvoke(() => OnTargetChanged(target));
            _controller.AppsUpdated += apps => _dispatcher.BeginInvoke(() =>
            {
                _state.Apps = apps;
                _state.Sessions = _media.Sessions;
            });
        }

        _preprocessor.Attach(_source);
        _worker.Start();
        _source.Start();
        _controller?.Start();
        _logger.LogInformation("Captions on");
    }

    private void StopCapturing()
    {
        if (!IsCapturing)
        {
            _state.IsOn = false;
            if (_state.Status != CaptionStatus.NoModel) _state.Status = CaptionStatus.Off;
            return;
        }

        _controller?.Dispose();
        _source?.Dispose();
        _worker?.Dispose();
        _controller = null;
        _source = null;
        _worker = null;
        _pauseAt = null;
        _tracker.Reset();
        _transcript.End();
        _view?.Clear();
        _pinner?.Follow(null, null);
        _state.IsOn = false;
        _state.Status = CaptionStatus.Off;
        _state.Target = null;
        _state.Media = null;
        _state.Thumbnail = null;
        _state.Apps = [];
        _logger.LogInformation("Captions off");
    }

    private void OnSettingsChanged(AppSettings old, AppSettings now)
    {
        if (old.Appearance != now.Appearance || old.Position != now.Position)
        {
            // Dragging the fixed spot changes only Position.Fixed, and the overlay is already there.
            var onlyFixed = old.Appearance == now.Appearance && old.Position with { Fixed = null } == now.Position with { Fixed = null };
            if (!onlyFixed) _view?.Apply(now);
            _pinner?.Apply(now);
        }

        if (old.Transcripts != now.Transcripts)
        {
            _transcript.Dispose();
            _transcript = NewTranscript(now);
            _transcript.Begin(_transcriptName.Title, _transcriptName.App);
        }

        if (old.Engine.Model != now.Engine.Model)
        {
            _state.ModelName = ShortName(now.Engine.Model);
            if (IsCapturing)
            {
                StopCapturing();
                SetOn(true);
            }
        }
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.ShowingLevels) && _controller != null) _controller.ShowingLevels = _state.ShowingLevels;
    }

    private TranscriptWriter NewTranscript(AppSettings settings) =>
        new(settings.Transcripts, _transcriptFolder ?? TranscriptNames.ResolveFolder(settings.Transcripts.Folder));

    private void BeginTranscript(string? title, string app)
    {
        _transcriptName = (title, app);
        _transcript.Begin(title, app);
    }

    private void OnCaption(CaptionEvent caption)
    {
        if (!IsCapturing) return;
        _view!.Show(caption);
        if (caption.Text.Length > 0) _state.LatestCaption = caption.Text;
        if (!caption.IsFinal || caption.Text.Length == 0) return;

        var now = DateTimeOffset.Now;
        var mediaNow = _tracker.Current is { HasTimeline: true } playing ? playing.PositionAt(now) : now - _transcript.BegunAt;
        var (start, end) = TranscriptFormat.Place(caption, mediaNow);
        _transcript.Add(caption.Text, start, end);
        _state.TranscriptPath = _transcript.CurrentFiles.FirstOrDefault(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) ?? _transcript.CurrentFiles.FirstOrDefault();
    }

    // Text from the old app must not run on into the new one.
    private void OnTargetChanged(CaptureTarget? target)
    {
        if (!IsCapturing) return;
        _worker!.RequestReset();
        _worker.Paused = false;
        _pauseAt = null;
        _view!.Clear();
        _tracker.Reset();
        _state.Target = target;
        _state.LatestCaption = null;
        _state.Status = target == null ? CaptionStatus.Waiting : CaptionStatus.Captioning;
        BeginTranscript(null, target?.DisplayName ?? "Captly");
    }

    private void OnMediaTick()
    {
        if (!IsCapturing || _controller == null) return;

        var target = _controller.Target;
        var sessions = _media.Sessions;
        var apps = _controller.Apps;
        LogUnknownApps(sessions, apps);

        var session = MediaMatching.SessionFor(target, sessions, apps);
        var update = _tracker.Update(session);
        var now = DateTimeOffset.Now;

        // All system audio has no app of its own, so the captions go on the media owner's window.
        var owner = target is { IsSystem: false } ? null : MediaMatching.PickOwner(sessions, apps);
        _pinner!.Follow(target?.RootPid ?? owner?.App.RootPid, session?.Title ?? owner?.Media.Title);

        if (update.NewMedia)
        {
            _logger.LogInformation("New media in {App}, transcript restarted", target?.DisplayName);
            Restart(paused: false);
            BeginTranscript(session!.Title, target?.DisplayName ?? "Captly");
            _ = LoadThumbnailAsync(session.AppId);
        }
        else if (update.Seeked)
        {
            _logger.LogInformation("Seek in {App}, captions cleared", target?.DisplayName);
            Restart(paused: _worker!.Paused);
            _transcript.Break();
        }

        if (update.Paused && Settings.General.PauseWithVideo) _pauseAt = now + PauseDelay;
        if (update.Resumed || session == null && _worker!.Paused)
        {
            if (_worker!.Paused) _logger.LogInformation("Playback resumed, captions on");
            _pauseAt = null;
            _worker.Paused = false;
        }

        // Waits a moment, so a quick pause and play does not blank the captions.
        if (_pauseAt is { } at && now >= at)
        {
            _pauseAt = null;
            if (session?.Status is PlaybackStatus.Paused or PlaybackStatus.Stopped)
            {
                _worker!.Paused = true;
                _view!.Clear();
                _logger.LogInformation("Playback paused, captions off");
            }
        }

        _state.Media = session;
        _state.Position = session?.PositionAt(now) ?? TimeSpan.Zero;
        if (session == null) _state.Thumbnail = null;
        _state.Status = _worker!.Paused ? CaptionStatus.Paused : target == null ? CaptionStatus.Waiting : CaptionStatus.Captioning;
    }

    private async Task LoadThumbnailAsync(string appId)
    {
        try
        {
            _state.Thumbnail = await _media.GetThumbnailAsync(appId, CancellationToken.None);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "No thumbnail for {AppId}", appId);
            _state.Thumbnail = null;
        }
    }

    private void Restart(bool paused)
    {
        _worker!.RequestReset();
        _worker.Paused = paused;
        _pauseAt = null;
        _view!.Clear();
        _state.LatestCaption = null;
    }

    private void LogUnknownApps(IReadOnlyList<MediaState> sessions, IReadOnlyList<AudioAppInfo> apps)
    {
        foreach (var session in sessions)
        {
            if (session.Status != PlaybackStatus.Playing || MediaMatching.AppFor(session, apps) != null || !_unknownApps.Add(session.AppId)) continue;
            _logger.LogInformation("Media from an app not matched to any sound: {AppId}", session.AppId);
        }
    }

    // "moonshine-tiny-streaming" becomes "Tiny model".
    private static string ShortName(string modelId)
    {
        var parts = modelId.Split('-');
        var name = parts.Length > 1 ? parts[1] : modelId;
        return name.Length == 0 ? "Speech model" : char.ToUpperInvariant(name[0]) + name[1..] + " model";
    }
}
