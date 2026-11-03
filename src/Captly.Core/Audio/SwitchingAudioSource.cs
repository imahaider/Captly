namespace Captly.Core.Audio;

/// <summary>
/// One fixed source for the pipeline that can change what it listens to. The old source is
/// disposed, which waits for its capture thread, before the new one starts, so the ring buffer
/// behind it only ever has one writer.
/// </summary>
public sealed class SwitchingAudioSource : IAudioSource
{
    private readonly object _gate = new();
    private IAudioSource? _inner;
    private bool _running;

    public string DisplayName => _inner?.DisplayName ?? "Nothing";
    public WaveFormatInfo Format => _inner?.Format ?? default;

    public event Action<WaveFormatInfo>? FormatChanged;
    public event Action<ReadOnlyMemory<float>>? SamplesAvailable;

    /// <summary>Stops the current source and starts this one, if the switcher is running. Null listens to nothing.</summary>
    public void SwitchTo(IAudioSource? next)
    {
        lock (_gate)
        {
            if (_inner != null)
            {
                _inner.FormatChanged -= OnFormatChanged;
                _inner.SamplesAvailable -= OnSamples;
                _inner.Dispose();
                _inner = null;
            }
            if (next == null) return;

            next.FormatChanged += OnFormatChanged;
            next.SamplesAvailable += OnSamples;
            try
            {
                if (_running) next.Start();
            }
            catch
            {
                next.FormatChanged -= OnFormatChanged;
                next.SamplesAvailable -= OnSamples;
                next.Dispose();
                throw;
            }
            _inner = next;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            _running = true;
            _inner?.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _inner?.Stop();
        }
    }

    public void Dispose() => SwitchTo(null);

    private void OnFormatChanged(WaveFormatInfo format) => FormatChanged?.Invoke(format);

    private void OnSamples(ReadOnlyMemory<float> samples) => SamplesAvailable?.Invoke(samples);
}
