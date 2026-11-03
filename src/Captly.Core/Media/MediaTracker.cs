namespace Captly.Core.Media;

/// <summary>What changed in the captured app's media since the last update.</summary>
public readonly record struct MediaUpdate(bool NewMedia, bool Paused, bool Resumed, bool Seeked)
{
    public static MediaUpdate None => default;
}

/// <summary>
/// Follows the captured app's media session and reports a new video, a pause, a resume or a
/// seek. A seek is a reported position more than two seconds from where playback should be.
/// </summary>
public sealed class MediaTracker
{
    public static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(2);

    public MediaState? Current { get; private set; }

    public void Reset() => Current = null;

    public MediaUpdate Update(MediaState? media)
    {
        var previous = Current;
        Current = media;
        if (media == null) return MediaUpdate.None;

        var stopped = media.Status is PlaybackStatus.Paused or PlaybackStatus.Stopped;

        // A title that is still loading belongs to the media before it, so it is not a new video.
        var newMedia = previous == null
            || previous.AppId != media.AppId
            || media.Title.Length > 0 && media.Title != previous.Title;
        if (newMedia) return new MediaUpdate(NewMedia: true, Paused: stopped, Resumed: false, Seeked: false);

        var wasPlaying = previous!.Status == PlaybackStatus.Playing;
        var isPlaying = media.Status == PlaybackStatus.Playing;
        var seeked = media.HasTimeline
            && media.PositionReportedAt != previous.PositionReportedAt
            && (media.Position - previous.PositionAt(media.PositionReportedAt)).Duration() > SeekThreshold;

        return new MediaUpdate(NewMedia: false, Paused: wasPlaying && stopped, Resumed: !wasPlaying && isPlaying, Seeked: seeked);
    }
}
