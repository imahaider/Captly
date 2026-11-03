using Captly.Core.Audio;
using Captly.Core.Speech;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Core.Tests;

public class CaptionWorkerTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(20);

    private sealed class FakeReader : ISampleReader
    {
        public Queue<float[]> Chunks { get; } = new();

        public int Read(Span<float> destination)
        {
            if (Chunks.Count == 0) return 0;
            var chunk = Chunks.Dequeue();
            chunk.CopyTo(destination);
            return chunk.Length;
        }
    }

    private sealed class FakeEngine : ISpeechEngine
    {
        public List<float[]> Pushed { get; } = [];
        public int Resets { get; private set; }
        public string Name => "Fake";
        public event Action<CaptionEvent>? Caption { add { } remove { } }
        public void Load(EngineOptions options) { }
        public void Push(ReadOnlySpan<float> audio16kMono) => Pushed.Add(audio16kMono.ToArray());
        public void Reset() => Resets++;
        public void Dispose() { }
    }

    private readonly FakeReader _reader = new();
    private readonly FakeEngine _engine = new();
    private readonly CaptionWorker _worker;

    public CaptionWorkerTests() => _worker = new CaptionWorker(_reader, _engine, NullLogger<CaptionWorker>.Instance);

    private void Idle(TimeSpan duration)
    {
        for (var passed = TimeSpan.Zero; passed < duration; passed += Tick) _worker.Step(Tick);
    }

    private static bool IsSilence(float[] chunk) => chunk.All(s => s == 0);

    [Fact]
    public void Audio_ReachesTheEngine()
    {
        _reader.Chunks.Enqueue([0.5f, 0.25f]);

        Assert.True(_worker.Step(Tick));

        Assert.Equal([0.5f, 0.25f], Assert.Single(_engine.Pushed));
    }

    [Fact]
    public void NeverHeardAnything_StaysCompletelyIdle()
    {
        Idle(TimeSpan.FromSeconds(10));
        Assert.Empty(_engine.Pushed);
    }

    [Fact]
    public void ShortGap_IsNotPadded()
    {
        _reader.Chunks.Enqueue([1f]);
        _worker.Step(Tick);

        Idle(CaptionWorker.GapBeforePadding - 2 * Tick);

        Assert.Single(_engine.Pushed);
    }

    [Fact]
    public void AfterAudioStops_SilenceFlowsAtRealTimePace()
    {
        _reader.Chunks.Enqueue([1f]);
        _worker.Step(Tick);

        Idle(CaptionWorker.GapBeforePadding + TimeSpan.FromSeconds(1));

        var silence = _engine.Pushed.Skip(1).ToList();
        Assert.All(silence, chunk => Assert.True(IsSilence(chunk)));
        var seconds = silence.Sum(c => c.Length) / (double)CaptionWorker.SampleRate;
        Assert.InRange(seconds, 0.9, 1.2);
    }

    [Fact]
    public void Padding_StopsSoIdleUsesNoCpu()
    {
        _reader.Chunks.Enqueue([1f]);
        _worker.Step(Tick);

        Idle(TimeSpan.FromSeconds(30));

        var seconds = _engine.Pushed.Skip(1).Sum(c => c.Length) / (double)CaptionWorker.SampleRate;
        Assert.Equal(CaptionWorker.MaxPadding.TotalSeconds, seconds, 1);
    }

    [Fact]
    public void AudioReturning_StartsPaddingAfresh()
    {
        _reader.Chunks.Enqueue([1f]);
        _worker.Step(Tick);
        Idle(TimeSpan.FromSeconds(30));
        var before = _engine.Pushed.Count;

        _reader.Chunks.Enqueue([1f]);
        _worker.Step(Tick);
        Idle(TimeSpan.FromSeconds(30));

        Assert.Equal(2 * before, _engine.Pushed.Count);
    }

    [Fact]
    public void RequestReset_RunsBeforeTheNextAudio()
    {
        _reader.Chunks.Enqueue([1f]);
        _worker.RequestReset();

        _worker.Step(Tick);

        Assert.Equal(1, _engine.Resets);
        Assert.Single(_engine.Pushed);
    }

    [Fact]
    public void Reset_CancelsPendingPadding()
    {
        _reader.Chunks.Enqueue([1f]);
        _worker.Step(Tick);

        _worker.RequestReset();
        Idle(TimeSpan.FromSeconds(5));

        Assert.Single(_engine.Pushed);
    }

    [Fact]
    public void Paused_DiscardsAudio()
    {
        _worker.Paused = true;
        _reader.Chunks.Enqueue([1f]);

        Assert.True(_worker.Step(Tick));

        Assert.Empty(_engine.Pushed);
        Assert.Empty(_reader.Chunks);
    }
}
