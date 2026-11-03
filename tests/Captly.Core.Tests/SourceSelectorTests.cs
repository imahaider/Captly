using Captly.Core.Audio;
using Captly.Core.Settings;

namespace Captly.Core.Tests;

public class SourceSelectorTests
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly SourceSettings Auto = new();

    private readonly FakeClock _clock = new();
    private readonly SourceSelector _selector;
    private readonly HashSet<int> _running = [100, 200, 300];

    public SourceSelectorTests() => _selector = new SourceSelector(_clock);

    private static AudioAppInfo App(int pid, float peak, string exe) => new(pid, exe, exe.Replace(".exe", ""), null, peak, null);

    private static readonly Func<float, AudioAppInfo> Chrome = peak => App(100, peak, "chrome.exe");
    private static readonly Func<float, AudioAppInfo> Spotify = peak => App(200, peak, "Spotify.exe");
    private static readonly Func<float, AudioAppInfo> Vlc = peak => App(300, peak, "vlc.exe");

    private bool Update(SourceSettings settings, params AudioAppInfo[] apps) =>
        _selector.Update(new SelectorInput(apps, settings, true, _running.Contains));

    private void Wait(double seconds) => _clock.Now += TimeSpan.FromSeconds(seconds);

    [Fact]
    public void WithoutPerAppCapture_UsesSystemAudio()
    {
        _selector.Update(new SelectorInput([Chrome(0.5f)], Auto, false, _running.Contains));
        Assert.True(_selector.Current!.IsSystem);
    }

    [Fact]
    public void SystemMode_UsesSystemAudio()
    {
        Update(Auto with { Mode = SourceMode.System }, Chrome(0.5f));
        Assert.True(_selector.Current!.IsSystem);
    }

    [Fact]
    public void Auto_WithNothingPlaying_ListensToNothing()
    {
        Assert.False(Update(Auto, Chrome(0)));
        Assert.Null(_selector.Current);
    }

    [Fact]
    public void Auto_PicksTheFirstAppThatPlays_AtOnce()
    {
        Assert.True(Update(Auto, Chrome(0.4f)));
        Assert.Equal(100, _selector.Current!.RootPid);
    }

    [Fact]
    public void Auto_SwitchesToALouderApp_OnlyAfterTheDelay()
    {
        Update(Auto, Chrome(0.4f));

        Assert.False(Update(Auto, Chrome(0.1f), Spotify(0.8f)));
        Wait(1.9);
        Assert.False(Update(Auto, Chrome(0.1f), Spotify(0.8f)));
        Wait(0.2);
        Assert.True(Update(Auto, Chrome(0.1f), Spotify(0.8f)));
        Assert.Equal(200, _selector.Current!.RootPid);
    }

    [Fact]
    public void Auto_ShortBurst_DoesNotStealCaptions()
    {
        Update(Auto, Chrome(0.4f));

        Update(Auto, Chrome(0.1f), Spotify(0.8f));
        Wait(1);
        Update(Auto, Chrome(0.4f), Spotify(0.1f));
        Wait(1.5);
        Update(Auto, Chrome(0.1f), Spotify(0.8f));

        Assert.Equal(100, _selector.Current!.RootPid);
    }

    [Fact]
    public void Auto_SwitchDelayComesFromSettings()
    {
        var quick = Auto with { SwitchDelayMs = 1000 };
        Update(quick, Chrome(0.4f));
        Update(quick, Chrome(0.1f), Spotify(0.8f));
        Wait(1.1);

        Assert.True(Update(quick, Chrome(0.1f), Spotify(0.8f)));
    }

    [Fact]
    public void Auto_KeepsAQuietTargetThatIsStillRunning()
    {
        Update(Auto, Chrome(0.4f));
        Wait(10);

        Assert.False(Update(Auto));
        Assert.Equal(100, _selector.Current!.RootPid);
    }

    [Fact]
    public void Auto_TargetExits_GoesStraightToTheNextApp()
    {
        Update(Auto, Chrome(0.4f));
        _running.Remove(100);

        Assert.True(Update(Auto, Spotify(0.3f)));
        Assert.Equal(200, _selector.Current!.RootPid);
    }

    [Fact]
    public void Auto_TargetExits_WithNothingElse_ListensToNothing()
    {
        Update(Auto, Chrome(0.4f));
        _running.Remove(100);

        Assert.True(Update(Auto));
        Assert.Null(_selector.Current);
    }

    [Fact]
    public void Auto_PrefersTheMediaOwner_EvenWhenQuieter()
    {
        _selector.Update(new SelectorInput([Chrome(0.1f), Spotify(0.9f)], Auto, true, _running.Contains, MediaOwnerPid: 100));
        Assert.Equal(100, _selector.Current!.RootPid);
    }

    [Fact]
    public void Auto_QuietTarget_GivesWayToTheMediaOwnerAtOnce()
    {
        Update(Auto, Chrome(0.4f));

        Assert.True(_selector.Update(new SelectorInput([Chrome(0), Spotify(0.3f)], Auto, true, _running.Contains, MediaOwnerPid: 200)));
        Assert.Equal(200, _selector.Current!.RootPid);
    }

    [Fact]
    public void Auto_LoudTarget_StillWaitsForTheMediaOwner()
    {
        Update(Auto, Chrome(0.4f));

        Assert.False(_selector.Update(new SelectorInput([Chrome(0.4f), Spotify(0.3f)], Auto, true, _running.Contains, MediaOwnerPid: 200)));
    }

    [Fact]
    public void Pinned_UsesThePinnedAppAtOnce_EvenWhenQuieter()
    {
        var pinned = Auto with { Mode = SourceMode.Pinned, PinnedExe = "vlc" };
        Update(pinned, Chrome(0.8f));
        Assert.Equal(100, _selector.Current!.RootPid);

        Assert.True(Update(pinned, Chrome(0.8f), Vlc(0.05f)));
        Assert.Equal(300, _selector.Current!.RootPid);
    }

    [Fact]
    public void Pinned_AppMissing_FallsBackToAuto_ThenReturns()
    {
        var pinned = Auto with { Mode = SourceMode.Pinned, PinnedExe = "vlc.exe" };
        Update(pinned, Spotify(0.5f));
        Assert.Equal(200, _selector.Current!.RootPid);

        Update(pinned, Spotify(0.5f), Vlc(0));
        Assert.Equal(300, _selector.Current!.RootPid);
    }

    [Fact]
    public void Pinned_QuietButRunning_IsKept()
    {
        var pinned = Auto with { Mode = SourceMode.Pinned, PinnedExe = "vlc.exe" };
        Update(pinned, Vlc(0.2f));
        Wait(10);

        Assert.False(Update(pinned, Spotify(0.9f)));
        Assert.Equal(300, _selector.Current!.RootPid);
    }

    [Fact]
    public void LeavingSystemMode_ChoosesAnApp()
    {
        Update(Auto with { Mode = SourceMode.System }, Chrome(0.5f));

        Assert.True(Update(Auto, Chrome(0.5f)));
        Assert.Equal(100, _selector.Current!.RootPid);
    }

    [Fact]
    public void Forget_LetsTheNextUpdateChooseAgain()
    {
        Update(Auto, Chrome(0.5f));
        _selector.Forget();

        Assert.True(Update(Auto, Chrome(0.5f)));
    }

    [Theory]
    [InlineData("chrome.exe", "chrome", true)]
    [InlineData("chrome.exe", "CHROME.EXE", true)]
    [InlineData("chrome.exe", "chromium", false)]
    [InlineData("chrome.exe", null, false)]
    public void SameExe_IgnoresCaseAndExtension(string a, string? b, bool expected) =>
        Assert.Equal(expected, SourceSelector.SameExe(a, b));
}
