using Captly.Core.Audio;

namespace Captly.Core.Tests;

public class ProcessTreeTests
{
    // explorer -> chrome (browser) -> chrome (renderer) -> chrome (audio utility)
    private static readonly ProcessTree Chrome = new(
    [
        new(1, 0, "System"),
        new(10, 1, "explorer.exe"),
        new(100, 10, "chrome.exe"),
        new(101, 100, "chrome.exe"),
        new(102, 101, "chrome.exe"),
        new(200, 10, "Spotify.exe"),
        new(201, 200, "Spotify.exe"),
        new(300, 10, "cmd.exe"),
        new(301, 300, "vlc.exe"),
    ]);

    [Theory]
    [InlineData(102, 100)]
    [InlineData(101, 100)]
    [InlineData(100, 100)]
    [InlineData(201, 200)]
    public void ChildProcesses_WalkUpToTheAppsRoot(int pid, int root) => Assert.Equal(root, Chrome.RootOf(pid));

    [Fact]
    public void WalkStops_WhenTheExeNameChanges() => Assert.Equal(301, Chrome.RootOf(301));

    [Fact]
    public void ExeNames_MatchIgnoringCase()
    {
        var tree = new ProcessTree([new(1, 0, "CHROME.EXE"), new(2, 1, "chrome.exe")]);
        Assert.Equal(1, tree.RootOf(2));
    }

    [Fact]
    public void UnknownProcess_IsItsOwnRoot() => Assert.Equal(999, Chrome.RootOf(999));

    [Fact]
    public void MissingParent_StopsTheWalk()
    {
        var tree = new ProcessTree([new(5, 4, "chrome.exe")]);
        Assert.Equal(5, tree.RootOf(5));
    }

    [Fact]
    public void ReusedPidLoop_DoesNotHang()
    {
        var tree = new ProcessTree([new(1, 2, "app.exe"), new(2, 1, "app.exe")]);
        Assert.Contains(tree.RootOf(1), new[] { 1, 2 });
    }

    [Fact]
    public void SelfParent_DoesNotHang()
    {
        var tree = new ProcessTree([new(7, 7, "app.exe")]);
        Assert.Equal(7, tree.RootOf(7));
    }

    [Fact]
    public void ExeName_IsLookedUp()
    {
        Assert.Equal("Spotify.exe", Chrome.ExeName(200));
        Assert.Null(Chrome.ExeName(999));
    }
}
