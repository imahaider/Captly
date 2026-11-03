using Captly.Core.Audio;

namespace Captly.Core.Tests;

public class AudioAppGrouperTests
{
    private const int Captly = 50;

    private static readonly ProcessTree Tree = new(
    [
        new(10, 1, "explorer.exe"),
        new(50, 10, "Captly.exe"),
        new(100, 10, "chrome.exe"),
        new(101, 100, "chrome.exe"),
        new(102, 100, "chrome.exe"),
        new(150, 10, "chrome.exe"),
        new(151, 150, "chrome.exe"),
        new(200, 10, "Spotify.exe"),
    ]);

    private static readonly Dictionary<int, string> Names = new() { [100] = "Google Chrome", [150] = "Google Chrome", [200] = "Spotify" };

    private static List<AudioAppInfo> Group(params SessionSample[] sessions) =>
        AudioAppGrouper.Group(sessions, Tree, Captly,
            (pid, exe) => new ProcessDetails(Names.GetValueOrDefault(pid, exe), $@"C:\Apps\{exe}"),
            pid => pid == 100 ? "Inside a jet engine - YouTube" : null);

    [Fact]
    public void ChildSessions_BecomeOneAppAtTheRoot()
    {
        var app = Assert.Single(Group(new(101, 0.2f, true), new(102, 0.6f, true)));

        Assert.Equal(100, app.RootPid);
        Assert.Equal("chrome.exe", app.ExeName);
        Assert.Equal("Google Chrome", app.DisplayName);
        Assert.Equal(@"C:\Apps\chrome.exe", app.IconPath);
        Assert.Equal(0.6f, app.PeakLevel);
    }

    [Fact]
    public void InactiveSessions_AreListedAsSilent()
    {
        var app = Assert.Single(Group(new SessionSample(200, 0.9f, false)));
        Assert.Equal(0, app.PeakLevel);
    }

    [Fact]
    public void SystemSoundsAndCaptly_AreLeftOut() =>
        Assert.Empty(Group(new(0, 0.5f, true), new(Captly, 0.5f, true)));

    [Fact]
    public void Apps_AreSortedLoudestFirst()
    {
        var apps = Group(new(200, 0.3f, true), new(101, 0.7f, true));
        Assert.Equal(["Google Chrome", "Spotify"], apps.Select(a => a.DisplayName));
    }

    [Fact]
    public void SameNamedApps_AreToldApartByWindowTitle()
    {
        var apps = Group(new(101, 0.5f, true), new(151, 0.4f, true), new(200, 0.1f, true));

        Assert.Equal("Inside a jet engine - YouTube", apps.Single(a => a.RootPid == 100).Subtitle);
        Assert.Equal("Process 150", apps.Single(a => a.RootPid == 150).Subtitle);
        Assert.Null(apps.Single(a => a.RootPid == 200).Subtitle);
    }

    [Fact]
    public void UniqueApps_HaveNoSubtitle() => Assert.Null(Group(new SessionSample(101, 0.5f, true))[0].Subtitle);

    [Fact]
    public void UnknownProcess_StillAppears()
    {
        var app = Assert.Single(Group(new SessionSample(999, 0.5f, true)));
        Assert.Equal("PID 999", app.ExeName);
    }
}
