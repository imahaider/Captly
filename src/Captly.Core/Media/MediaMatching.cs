using Captly.Core.Audio;

namespace Captly.Core.Media;

/// <summary>
/// Links media sessions, named by app ID, to the apps making sound, named by exe. Pure, so every
/// rule is unit-tested.
/// </summary>
public static class MediaMatching
{
    private const int MinimumStemLength = 4;

    /// <summary>
    /// App IDs that do not name their exe. Only "Chrome" was seen on this PC (DECISIONS.md D10).
    /// The rest are documented values, not yet seen here. Packaged app IDs match by prefix.
    /// </summary>
    private static readonly (string AppId, string Exe)[] KnownApps =
    [
        ("Chrome", "chrome.exe"),
        ("MSEdge", "msedge.exe"),
        ("308046B0AF4A39CB", "firefox.exe"),
        ("Brave", "brave.exe"),
        ("Opera", "opera.exe"),
        ("SpotifyAB.SpotifyMusic", "Spotify.exe"),
        ("Microsoft.ZuneMusic", "Microsoft.Media.Player.exe"),
        ("Microsoft.ZuneVideo", "Video.UI.exe"),
    ];

    /// <summary>Apps that mostly play music, so a video elsewhere is preferred over them.</summary>
    private static readonly HashSet<string> MusicApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Spotify.exe", "AppleMusic.exe", "iTunes.exe", "Deezer.exe", "TIDAL.exe", "Amazon Music.exe",
        "foobar2000.exe", "MusicBee.exe", "winamp.exe", "AIMP.exe",
    };

    public static bool IsMusicApp(string exeName) => MusicApps.Contains(exeName);

    /// <summary>The app that owns this session, or null if no app making sound matches.</summary>
    public static AudioAppInfo? AppFor(MediaState media, IReadOnlyList<AudioAppInfo> apps)
    {
        var exe = ExeFor(media.AppId);
        if (exe != null)
        {
            var byExe = apps.Where(a => SourceSelector.SameExe(a.ExeName, exe)).MaxBy(a => a.PeakLevel);
            if (byExe != null) return byExe;
        }

        // Packaged apps use IDs such as "Publisher.AppName_hash!App", which usually contain the exe's name.
        return apps
            .Where(a => Mentions(media.AppId, Stem(a.ExeName)) || Mentions(media.AppId, a.DisplayName.Replace(" ", "")))
            .MaxBy(a => a.PeakLevel);
    }

    /// <summary>
    /// The session Auto should caption, if any. A video app beats a music app, playing beats
    /// paused, and Windows' current session breaks a tie. So YouTube is captioned over Spotify,
    /// and a paused video keeps its place while music plays.
    /// </summary>
    public static (MediaState Media, AudioAppInfo App)? PickOwner(IReadOnlyList<MediaState> sessions, IReadOnlyList<AudioAppInfo> apps) =>
        Ranked(sessions, apps).Cast<(MediaState, AudioAppInfo)?>().FirstOrDefault();

    /// <summary>The session that belongs to the target, if it has one.</summary>
    public static MediaState? SessionFor(CaptureTarget? target, IReadOnlyList<MediaState> sessions, IReadOnlyList<AudioAppInfo> apps)
    {
        if (target is not { IsSystem: false }) return null;
        return Ranked(sessions, apps).Where(p => p.App.RootPid == target.RootPid).Select(p => p.Media).FirstOrDefault();
    }

    private static IEnumerable<(MediaState Media, AudioAppInfo App)> Ranked(IReadOnlyList<MediaState> sessions, IReadOnlyList<AudioAppInfo> apps) =>
        sessions
            .Where(s => s.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
            .Select(s => (Media: s, App: AppFor(s, apps)))
            .Where(p => p.App != null)
            .Select(p => (p.Media, App: p.App!))
            .OrderBy(p => IsMusicApp(p.App.ExeName))
            .ThenBy(p => p.Media.Status != PlaybackStatus.Playing)
            .ThenBy(p => !p.Media.IsCurrent);

    private static string? ExeFor(string appId)
    {
        foreach (var (known, exe) in KnownApps)
            if (appId.Equals(known, StringComparison.OrdinalIgnoreCase) || appId.StartsWith(known + "_", StringComparison.OrdinalIgnoreCase))
                return exe;

        // Some desktop apps use their exe name or path as the ID.
        return appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appId[(appId.LastIndexOfAny(['\\', '/']) + 1)..] : null;
    }

    private static string Stem(string exeName) => exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName[..^4] : exeName;

    private static bool Mentions(string appId, string name) =>
        name.Length >= MinimumStemLength && appId.Contains(name, StringComparison.OrdinalIgnoreCase);
}
