using Captly.Core.Audio;
using Captly.Core.Media;

namespace Captly.Core.Tests;

public class MediaStateTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static MediaState State(PlaybackStatus status, double position, double duration = 600, DateTimeOffset? reported = null) =>
        new("Chrome", "Video", "", status, TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration), reported ?? At, false);

    [Fact]
    public void Playing_PositionMovesOnFromTheLastReport() =>
        Assert.Equal(TimeSpan.FromSeconds(138), State(PlaybackStatus.Playing, 100).PositionAt(At.AddSeconds(38)));

    [Fact]
    public void Paused_PositionStaysPut() =>
        Assert.Equal(TimeSpan.FromSeconds(100), State(PlaybackStatus.Paused, 100).PositionAt(At.AddSeconds(38)));

    [Fact]
    public void Position_StopsAtTheEnd() =>
        Assert.Equal(TimeSpan.FromSeconds(600), State(PlaybackStatus.Playing, 590).PositionAt(At.AddSeconds(60)));

    [Fact]
    public void NeverReported_IsNotCarriedForward() =>
        Assert.Equal(TimeSpan.FromSeconds(5), State(PlaybackStatus.Playing, 5, reported: default(DateTimeOffset)).PositionAt(At));
}

public class MediaMatchingTests
{
    private static AudioAppInfo App(int pid, string exe, string name, float peak = 0.2f) => new(pid, exe, name, null, peak, null);

    private static readonly AudioAppInfo Chrome = App(100, "chrome.exe", "Google Chrome");
    private static readonly AudioAppInfo Spotify = App(200, "Spotify.exe", "Spotify", 0.8f);
    private static readonly AudioAppInfo Player = App(300, "Microsoft.Media.Player.exe", "Media Player");
    private static readonly AudioAppInfo Foobar = App(400, "foobar2000.exe", "foobar2000");
    private static readonly AudioAppInfo[] Apps = [Chrome, Spotify, Player, Foobar];

    private static MediaState Session(string appId, PlaybackStatus status = PlaybackStatus.Playing, bool current = false) =>
        new(appId, "Title", "", status, TimeSpan.Zero, TimeSpan.FromMinutes(5), default, current);

    [Theory]
    [InlineData("Chrome", 100)]
    [InlineData("chrome", 100)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", 200)]
    [InlineData("Spotify.exe", 200)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", 300)]
    [InlineData(@"C:\Program Files\foobar2000\foobar2000.exe", 400)]
    public void AppIds_MatchTheirApps(string appId, int pid) => Assert.Equal(pid, MediaMatching.AppFor(Session(appId), Apps)!.RootPid);

    [Fact]
    public void UnknownAppId_MatchesNothing() => Assert.Null(MediaMatching.AppFor(Session("Contoso.Player_abc!App"), Apps));

    [Fact]
    public void ShortNames_DoNotMatchByAccident()
    {
        AudioAppInfo[] apps = [App(500, "vlc.exe", "VLC")];
        Assert.Null(MediaMatching.AppFor(Session("Acme.Vlcx.Something"), apps));
    }

    [Fact]
    public void Owner_IsTheVideo_WhenMusicAlsoPlays()
    {
        var owner = MediaMatching.PickOwner([Session("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", current: true), Session("Chrome")], Apps);
        Assert.Equal(100, owner!.Value.App.RootPid);
    }

    [Fact]
    public void Owner_PausedVideo_KeepsItsPlaceOverMusic()
    {
        var owner = MediaMatching.PickOwner([Session("Spotify.exe"), Session("Chrome", PlaybackStatus.Paused)], Apps);
        Assert.Equal(100, owner!.Value.App.RootPid);
    }

    [Fact]
    public void Owner_MusicAlone_IsStillTheOwner()
    {
        var owner = MediaMatching.PickOwner([Session("Spotify.exe")], Apps);
        Assert.Equal(200, owner!.Value.App.RootPid);
    }

    [Fact]
    public void Owner_PrefersPlayingOverPaused_ThenWindowsCurrentSession()
    {
        Assert.Equal(300, MediaMatching.PickOwner([Session("Chrome", PlaybackStatus.Paused), Session("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")], Apps)!.Value.App.RootPid);
        Assert.Equal(300, MediaMatching.PickOwner([Session("Chrome"), Session("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", current: true)], Apps)!.Value.App.RootPid);
    }

    [Fact]
    public void Owner_IgnoresStoppedSessions_AndAppsWithoutSound()
    {
        Assert.Null(MediaMatching.PickOwner([Session("Chrome", PlaybackStatus.Stopped)], Apps));
        Assert.Null(MediaMatching.PickOwner([Session("Chrome")], [Spotify]));
    }

    [Fact]
    public void SessionFor_FindsTheTargetsSession()
    {
        var target = CaptureTarget.For(Chrome);
        var session = MediaMatching.SessionFor(target, [Session("Spotify.exe"), Session("Chrome", PlaybackStatus.Paused)], Apps);
        Assert.Equal("Chrome", session!.AppId);
    }

    [Fact]
    public void SessionFor_SystemAudioOrNothing_HasNone()
    {
        Assert.Null(MediaMatching.SessionFor(CaptureTarget.SystemAudio, [Session("Chrome")], Apps));
        Assert.Null(MediaMatching.SessionFor(null, [Session("Chrome")], Apps));
    }
}

public class MediaTrackerTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly MediaTracker _tracker = new();

    private static MediaState State(string title = "Video", PlaybackStatus status = PlaybackStatus.Playing, double position = 100, double reportedSeconds = 0) =>
        new("Chrome", title, "", status, TimeSpan.FromSeconds(position), TimeSpan.FromMinutes(30), At.AddSeconds(reportedSeconds), true);

    [Fact]
    public void FirstSighting_IsNewMedia() => Assert.True(_tracker.Update(State()).NewMedia);

    [Fact]
    public void FirstSighting_WhilePaused_IsAlsoAPause()
    {
        var update = _tracker.Update(State(status: PlaybackStatus.Paused));
        Assert.True(update.NewMedia);
        Assert.True(update.Paused);
    }

    [Fact]
    public void SameMedia_ReportsNothing()
    {
        _tracker.Update(State());
        Assert.Equal(MediaUpdate.None, _tracker.Update(State()));
    }

    [Fact]
    public void TitleChange_IsNewMedia()
    {
        _tracker.Update(State());
        Assert.True(_tracker.Update(State("Next video")).NewMedia);
    }

    [Fact]
    public void TitleStillLoading_IsNotNewMedia()
    {
        _tracker.Update(State());
        Assert.False(_tracker.Update(State("")).NewMedia);
    }

    [Fact]
    public void PauseAndResume_AreReported()
    {
        _tracker.Update(State());

        Assert.True(_tracker.Update(State(status: PlaybackStatus.Paused, position: 110, reportedSeconds: 10)).Paused);
        Assert.True(_tracker.Update(State(position: 110, reportedSeconds: 30)).Resumed);
    }

    [Fact]
    public void NormalProgress_IsNotASeek()
    {
        _tracker.Update(State(position: 100));
        Assert.False(_tracker.Update(State(position: 130.5, reportedSeconds: 30)).Seeked);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(20)]
    public void JumpOfMoreThanTwoSeconds_IsASeek(double newPosition)
    {
        _tracker.Update(State(position: 100));
        Assert.True(_tracker.Update(State(position: newPosition, reportedSeconds: 10)).Seeked);
    }

    [Fact]
    public void StaleReport_IsNeverASeek()
    {
        _tracker.Update(State(position: 100));
        Assert.False(_tracker.Update(State(position: 100)).Seeked);
    }

    [Fact]
    public void SeekWhilePaused_IsCaught()
    {
        _tracker.Update(State(status: PlaybackStatus.Paused, position: 100));
        Assert.True(_tracker.Update(State(status: PlaybackStatus.Paused, position: 300, reportedSeconds: 5)).Seeked);
    }

    [Fact]
    public void LosingTheSession_ThenFindingItAgain_IsNewMedia()
    {
        _tracker.Update(State());
        Assert.Equal(MediaUpdate.None, _tracker.Update(null));
        Assert.True(_tracker.Update(State()).NewMedia);
    }
}
