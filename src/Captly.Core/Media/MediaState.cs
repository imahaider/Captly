namespace Captly.Core.Media;

public enum PlaybackStatus { Unknown, Playing, Paused, Stopped }

/// <summary>One media session as Windows reports it.</summary>
public sealed record MediaState(
    string AppId,
    string Title,
    string Artist,
    PlaybackStatus Status,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionReportedAt,
    bool IsCurrent)
{
    public bool HasTimeline => Duration > TimeSpan.Zero;

    /// <summary>
    /// The position now. Players such as Chrome report it rarely, so while playing it is carried
    /// forward from the last report (DECISIONS.md D10), and kept within the media's length.
    /// </summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        if (Status != PlaybackStatus.Playing || PositionReportedAt == default) return Position;
        var position = Position + (now - PositionReportedAt);
        if (position < TimeSpan.Zero) return TimeSpan.Zero;
        return HasTimeline && position > Duration ? Duration : position;
    }
}

public interface IMediaWatcher : IDisposable
{
    /// <summary>The latest snapshot of every session. Replaced as a whole, so it can be read from any thread.</summary>
    IReadOnlyList<MediaState> Sessions { get; }

    /// <summary>Raised on a background thread when <see cref="Sessions"/> changes.</summary>
    event Action? Changed;

    void Start();

    /// <summary>The session's artwork as encoded image bytes, or null.</summary>
    Task<byte[]?> GetThumbnailAsync(string appId, CancellationToken ct);
}
