using Captly.Core.Audio;
using Captly.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Core.Tests;

internal sealed class FakeSource(string name) : IAudioSource
{
    public string DisplayName => name;
    public WaveFormatInfo Format => new(48000, 2);
    public bool Running { get; private set; }
    public bool Disposed { get; private set; }
    public bool FailOnStart { get; init; }
    public event Action<WaveFormatInfo>? FormatChanged;
    public event Action<ReadOnlyMemory<float>>? SamplesAvailable;

    public void Start()
    {
        if (FailOnStart) throw new InvalidOperationException("Activation failed");
        Running = true;
    }

    public void Stop() => Running = false;

    public void Dispose()
    {
        Running = false;
        Disposed = true;
    }

    public void Emit(float value)
    {
        FormatChanged?.Invoke(Format);
        SamplesAvailable?.Invoke(new[] { value });
    }
}

public class SwitchingAudioSourceTests
{
    [Fact]
    public void ForwardsTheCurrentSource_AndDropsTheOld()
    {
        var switcher = new SwitchingAudioSource();
        var received = new List<float>();
        switcher.SamplesAvailable += s => received.Add(s.Span[0]);
        var first = new FakeSource("A");
        var second = new FakeSource("B");

        switcher.SwitchTo(first);
        first.Emit(1);
        switcher.SwitchTo(second);
        first.Emit(2);
        second.Emit(3);

        Assert.Equal([1f, 3f], received);
        Assert.True(first.Disposed);
        Assert.Equal("B", switcher.DisplayName);
    }

    [Fact]
    public void NewSource_StartsOnlyIfTheSwitcherIsRunning()
    {
        var switcher = new SwitchingAudioSource();
        var idle = new FakeSource("A");
        switcher.SwitchTo(idle);
        Assert.False(idle.Running);

        switcher.Start();
        Assert.True(idle.Running);

        var next = new FakeSource("B");
        switcher.SwitchTo(next);
        Assert.True(next.Running);
    }

    [Fact]
    public void FormatChanges_AreForwarded()
    {
        var switcher = new SwitchingAudioSource();
        WaveFormatInfo? seen = null;
        switcher.FormatChanged += f => seen = f;
        var source = new FakeSource("A");
        switcher.SwitchTo(source);

        source.Emit(0);

        Assert.Equal(new WaveFormatInfo(48000, 2), seen);
    }

    [Fact]
    public void FailedStart_LeavesNothingAttached()
    {
        var switcher = new SwitchingAudioSource();
        switcher.Start();
        var broken = new FakeSource("A") { FailOnStart = true };

        Assert.Throws<InvalidOperationException>(() => switcher.SwitchTo(broken));

        Assert.True(broken.Disposed);
        Assert.Equal("Nothing", switcher.DisplayName);
    }

    [Fact]
    public void SwitchToNothing_DisposesTheSource()
    {
        var switcher = new SwitchingAudioSource();
        var source = new FakeSource("A");
        switcher.SwitchTo(source);

        switcher.SwitchTo(null);

        Assert.True(source.Disposed);
    }
}

public class SourceControllerTests
{
    private sealed class FakeFactory : IAudioSourceFactory
    {
        public bool SupportsPerProcess { get; set; } = true;
        public List<AudioAppInfo> Apps { get; set; } = [];
        public List<string> Created { get; } = [];
        public HashSet<int> Broken { get; } = [];

        public IReadOnlyList<AudioAppInfo> ListActiveApps() => Apps;

        public IAudioSource CreateForProcess(int rootPid)
        {
            Created.Add($"app {rootPid}");
            return new FakeSource($"app {rootPid}") { FailOnStart = Broken.Contains(rootPid) };
        }

        public IAudioSource CreateSystemWide()
        {
            Created.Add("system");
            return new FakeSource("system");
        }

        public bool IsRunning(int pid) => Apps.Any(a => a.RootPid == pid);
    }

    private readonly FakeFactory _factory = new();
    private readonly SwitchingAudioSource _output = new();
    private SourceSettings _settings = new();
    private readonly SourceController _controller;
    private readonly List<CaptureTarget?> _changes = [];

    public SourceControllerTests()
    {
        _output.Start();
        _controller = new SourceController(_factory, _output, () => _settings, NullLogger<SourceController>.Instance);
        _controller.TargetChanged += _changes.Add;
    }

    private static AudioAppInfo App(int pid, float peak) => new(pid, $"app{pid}.exe", $"App {pid}", null, peak, null);

    [Fact]
    public void NewTarget_StartsCaptureAndReports()
    {
        _factory.Apps = [App(100, 0.5f)];

        _controller.Poll();

        Assert.Equal(["app 100"], _factory.Created);
        Assert.Equal(100, Assert.Single(_changes)!.RootPid);
        Assert.Equal("app 100", _output.DisplayName);
    }

    [Fact]
    public void SameTarget_DoesNotRestartCapture()
    {
        _factory.Apps = [App(100, 0.5f)];

        _controller.Poll();
        _controller.Poll();

        Assert.Single(_factory.Created);
    }

    [Fact]
    public void WithoutPerAppCapture_CapturesSystemAudio()
    {
        _factory.SupportsPerProcess = false;
        _factory.Apps = [App(100, 0.5f)];

        _controller.Poll();

        Assert.Equal(["system"], _factory.Created);
        Assert.Empty(_controller.Apps);
    }

    [Fact]
    public void FailedCapture_ReportsNothing_AndTriesAgain()
    {
        _factory.Apps = [App(100, 0.5f)];
        _factory.Broken.Add(100);

        _controller.Poll();
        Assert.Null(Assert.Single(_changes));
        Assert.Equal("Nothing", _output.DisplayName);

        _factory.Broken.Clear();
        _controller.Poll();
        Assert.Equal("app 100", _output.DisplayName);
    }

    [Fact]
    public void TargetGone_StopsCapture()
    {
        _factory.Apps = [App(100, 0.5f)];
        _controller.Poll();

        _factory.Apps = [];
        _controller.Poll();

        Assert.Null(_changes[^1]);
        Assert.Equal("Nothing", _output.DisplayName);
    }

    [Fact]
    public void SettingsAreReadEachPoll()
    {
        _factory.Apps = [App(100, 0.5f)];
        _controller.Poll();

        _settings = _settings with { Mode = SourceMode.System };
        _controller.Poll();

        Assert.True(_changes[^1]!.IsSystem);
    }
}
