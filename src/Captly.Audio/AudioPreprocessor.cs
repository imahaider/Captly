using Captly.Core.Audio;

namespace Captly.Audio;

/// <summary>
/// Turns a source's stream into 16 kHz mono in a ring buffer. <see cref="Process"/> runs on the
/// capture thread and allocates nothing. Only a format change allocates.
/// </summary>
public sealed class AudioPreprocessor(int bufferSeconds = 30) : ISampleReader
{
    public const int OutputRate = 16000;
    private const int BlockFrames = 2048;

    private readonly FloatRingBuffer _ring = new(bufferSeconds * OutputRate);
    private readonly float[] _mono = new float[BlockFrames];
    private WaveFormatInfo _format;
    private Resampler? _resampler;
    private float[] _resampled = [];
    private long _dropped;

    public int Available => _ring.Available;

    /// <summary>Samples lost because nothing read the buffer in time.</summary>
    public long DroppedSamples => Volatile.Read(ref _dropped);

    public void Attach(IAudioSource source)
    {
        source.FormatChanged += Configure;
        source.SamplesAvailable += samples => Process(samples.Span);
    }

    public void Configure(WaveFormatInfo format)
    {
        if (format == _format) return;
        _format = format;
        _resampler = new Resampler(format.SampleRate, OutputRate);
        _resampled = new float[_resampler.MaxOutputFrames(BlockFrames)];
    }

    public void Process(ReadOnlySpan<float> interleaved)
    {
        if (_resampler == null) throw new InvalidOperationException("Configure must run before Process.");

        var blockSamples = BlockFrames * _format.Channels;
        while (!interleaved.IsEmpty)
        {
            var block = interleaved[..Math.Min(blockSamples, interleaved.Length)];
            interleaved = interleaved[block.Length..];

            var frames = block.Length / _format.Channels;
            Downmixer.ToMono(block, _format.Channels, _mono);
            var produced = _resampler.Process(_mono.AsSpan(0, frames), _resampled);
            var stored = _ring.Write(_resampled.AsSpan(0, produced));
            if (stored < produced) Interlocked.Add(ref _dropped, produced - stored);
        }
    }

    /// <summary>Reads 16 kHz mono samples. Call from one consumer thread only.</summary>
    public int Read(Span<float> destination) => _ring.Read(destination);
}
