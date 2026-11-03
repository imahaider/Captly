using Captly.Audio;
using Captly.Core.Media;
using Captly.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Bench;

/// <summary>Shows what Windows reports as playing, which app each session maps to, and which Auto would caption.</summary>
internal static class MediaCheck
{
    public static int Run(int seconds)
    {
        using var factory = new AudioSourceFactory(NullLoggerFactory.Instance);
        using var watcher = new SmtcMediaWatcher(NullLogger<SmtcMediaWatcher>.Instance);
        watcher.Start();
        Thread.Sleep(1000);

        for (var i = 0; i < seconds; i++)
        {
            var apps = factory.ListActiveApps();
            var sessions = watcher.Sessions;
            var owner = MediaMatching.PickOwner(sessions, apps);
            Console.WriteLine($"[{i,3}s] {sessions.Count} session(s), owner: {owner?.App.DisplayName ?? "none"}");
            foreach (var session in sessions)
            {
                // Titles are the user's own viewing, so only their length is shown.
                var app = MediaMatching.AppFor(session, apps);
                Console.WriteLine($"       {session.AppId,-30} {session.Status,-8} {session.PositionAt(DateTimeOffset.Now):hh\\:mm\\:ss}/{session.Duration:hh\\:mm\\:ss}" +
                                  $"  title {session.Title.Length} chars  current {session.IsCurrent,-5}  app: {app?.DisplayName ?? "not matched"}");
            }
            Thread.Sleep(1000);
        }
        return 0;
    }

    /// <summary>
    /// Publishes a media session of its own, playing a speech WAV quietly, and scripts it: play,
    /// pause, resume, seek, then a new title. Run Captly in Auto alongside to watch it follow.
    /// </summary>
    public static async Task<int> Play(string wav)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(wav));
        using var player = new Windows.Media.Playback.MediaPlayer { Volume = 0.3 };
        var item = new Windows.Media.Playback.MediaPlaybackItem(Windows.Media.Core.MediaSource.CreateFromStorageFile(file));
        SetTitle(item, "Captly media test");
        player.Source = item;

        async Task Step(double atSeconds, string what, Action action)
        {
            var wait = TimeSpan.FromSeconds(atSeconds) - Clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            action();
            Console.WriteLine($"{Clock.Elapsed.TotalSeconds,5:F1}s {what}");
        }

        Clock.Restart();
        await Step(0, "play from 0:00", player.Play);
        await Step(8, "pause", player.Pause);
        await Step(12, "resume", player.Play);
        await Step(18, "seek to 0:30", () => player.PlaybackSession.Position = TimeSpan.FromSeconds(30));
        await Step(24, "new title, back to 0:00", () =>
        {
            SetTitle(item, "Captly media test, part two");
            player.PlaybackSession.Position = TimeSpan.Zero;
        });
        await Step(32, "stop", player.Pause);
        return 0;
    }

    private static readonly System.Diagnostics.Stopwatch Clock = new();

    private static void SetTitle(Windows.Media.Playback.MediaPlaybackItem item, string title)
    {
        var properties = item.GetDisplayProperties();
        properties.Type = Windows.Media.MediaPlaybackType.Video;
        properties.VideoProperties.Title = title;
        item.ApplyDisplayProperties(properties);
    }
}
