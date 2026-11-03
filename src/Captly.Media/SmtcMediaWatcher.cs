using Captly.Core.Media;
using Microsoft.Extensions.Logging;
using Windows.Foundation;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace Captly.Media;

/// <summary>
/// Follows Windows' media sessions (System Media Transport Controls). Every session event leads
/// to one fresh snapshot. Refreshes never overlap, and a burst of events collapses into one more.
/// </summary>
public sealed class SmtcMediaWatcher(ILogger<SmtcMediaWatcher> logger) : IMediaWatcher
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _watched = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private int _refreshing;
    private int _dirty;

    public IReadOnlyList<MediaState> Sessions { get; private set; } = [];

    public event Action? Changed;

    public void Start() => _ = Task.Run(async () =>
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            Watch();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Media sessions are unavailable. Captions will not follow play and pause");
        }
    });

    public async Task<byte[]?> GetThumbnailAsync(string appId, CancellationToken ct)
    {
        var session = _manager?.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == appId);
        if (session == null) return null;
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(ct);
            if (properties?.Thumbnail == null) return null;
            using var stream = await properties.Thumbnail.OpenReadAsync().AsTask(ct);
            await using var source = stream.AsStreamForRead();
            using var bytes = new MemoryStream();
            await source.CopyToAsync(bytes, ct);
            return bytes.ToArray();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogDebug(e, "No thumbnail for {AppId}", appId);
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var session in _watched.Values) Unsubscribe(session);
            _watched.Clear();
            if (_manager == null) return;
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs e) => Watch();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs e) => Refresh();

    private void OnPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs e) => Refresh();

    private void OnPlaybackChanged(Session sender, PlaybackInfoChangedEventArgs e) => Refresh();

    private void OnTimelineChanged(Session sender, TimelinePropertiesChangedEventArgs e) => Refresh();

    // Subscribes to sessions that appeared, and lets go of those that ended.
    private void Watch()
    {
        if (_manager == null) return;
        lock (_gate)
        {
            var current = _manager.GetSessions().ToDictionary(s => s.SourceAppUserModelId);
            foreach (var gone in _watched.Keys.Except(current.Keys).ToList())
            {
                Unsubscribe(_watched[gone]);
                _watched.Remove(gone);
            }
            foreach (var (id, session) in current)
            {
                if (_watched.ContainsKey(id)) continue;
                session.MediaPropertiesChanged += OnPropertiesChanged;
                session.PlaybackInfoChanged += OnPlaybackChanged;
                session.TimelinePropertiesChanged += OnTimelineChanged;
                _watched[id] = session;
            }
        }
        Refresh();
    }

    private void Unsubscribe(Session session)
    {
        session.MediaPropertiesChanged -= OnPropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackChanged;
        session.TimelinePropertiesChanged -= OnTimelineChanged;
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _dirty, 1);
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                while (Interlocked.Exchange(ref _dirty, 0) == 1) await ReadAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
            if (Volatile.Read(ref _dirty) == 1) Refresh();
        });
    }

    private async Task ReadAsync()
    {
        if (_manager == null) return;
        try
        {
            var currentId = _manager.GetCurrentSession()?.SourceAppUserModelId;
            var states = new List<MediaState>();
            foreach (var session in _manager.GetSessions())
            {
                GlobalSystemMediaTransportControlsSessionMediaProperties? properties = null;
                try
                {
                    properties = await session.TryGetMediaPropertiesAsync();
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "No media properties for {AppId}", session.SourceAppUserModelId);
                }

                var timeline = session.GetTimelineProperties();
                states.Add(new MediaState(
                    session.SourceAppUserModelId,
                    properties?.Title ?? "",
                    properties?.Artist ?? "",
                    Map(session.GetPlaybackInfo().PlaybackStatus),
                    timeline.Position,
                    timeline.EndTime - timeline.StartTime,
                    timeline.LastUpdatedTime,
                    session.SourceAppUserModelId == currentId));
            }
            Sessions = states;
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read media sessions");
        }
    }

    private static PlaybackStatus Map(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlaybackStatus.Stopped,
        _ => PlaybackStatus.Unknown,
    };
}
