using Captly.Core.Audio;
using Microsoft.Extensions.Logging;

namespace Captly.Core.Speech;

/// <summary>
/// The inference thread: moves audio from the reader into the engine. Loopback delivers nothing
/// while a video is silent, but the engine only ends a line when it hears silence, so a short
/// run of real-time silence is fed after audio stops. Without it, the last words would stay
/// unfinished on screen.
/// </summary>
public sealed class CaptionWorker : IDisposable
{
    public const int SampleRate = 16000;
    private const int ChunkSamples = SampleRate / 10;

    public static readonly TimeSpan GapBeforePadding = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan MaxPadding = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    // While paused, or once a silence has been padded out, audio is checked ten times a second
    // instead of fifty. The ring buffer keeps what arrives meanwhile, so nothing is lost.
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ISampleReader _reader;
    private readonly ISpeechEngine _engine;
    private readonly ILogger<CaptionWorker> _logger;
    private readonly TimeProvider _clock;
    private readonly Thread _thread;
    private readonly float[] _buffer = new float[ChunkSamples];
    private readonly float[] _silence = new float[ChunkSamples];
    private volatile bool _stopping;
    private volatile bool _resetRequested;
    private volatile bool _paused;
    private TimeSpan _idle;
    private TimeSpan _padded;
    private bool _heardAudio;

    public CaptionWorker(ISampleReader reader, ISpeechEngine engine, ILogger<CaptionWorker> logger, TimeProvider? clock = null)
    {
        _reader = reader;
        _engine = engine;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _thread = new Thread(Run) { IsBackground = true, Name = "Captly inference" };
    }

    public void Start() => _thread.Start();

    /// <summary>Safe from any thread. The engine resets on the inference thread before the next audio.</summary>
    public void RequestReset() => _resetRequested = true;

    /// <summary>While paused, audio is read and thrown away, so the engine hears nothing of it.</summary>
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public void Dispose()
    {
        _stopping = true;
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        var last = _clock.GetTimestamp();
        while (!_stopping)
        {
            var now = _clock.GetTimestamp();
            var elapsed = _clock.GetElapsedTime(last, now);
            last = now;
            try
            {
                if (Step(elapsed)) continue;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Speech engine failed on this audio");
            }
            Thread.Sleep(_paused || !_heardAudio ? IdlePollInterval : PollInterval);
        }
    }

    /// <summary>Does one unit of work. Returns true if more may be ready now.</summary>
    internal bool Step(TimeSpan elapsed)
    {
        if (_resetRequested)
        {
            _resetRequested = false;
            _engine.Reset();
            _heardAudio = false;
        }

        var count = _reader.Read(_buffer);
        if (_paused) return count > 0;

        if (count > 0)
        {
            _engine.Push(_buffer.AsSpan(0, count));
            _heardAudio = true;
            _idle = TimeSpan.Zero;
            _padded = TimeSpan.Zero;
            return true;
        }

        if (!_heardAudio) return false;

        _idle += elapsed;
        if (_idle < GapBeforePadding + _padded) return false;

        _engine.Push(_silence);
        _padded += TimeSpan.FromSeconds(ChunkSamples / (double)SampleRate);
        if (_padded >= MaxPadding) _heardAudio = false;
        return false;
    }
}
