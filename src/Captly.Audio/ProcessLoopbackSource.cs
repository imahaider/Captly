using Captly.Core.Audio;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Captly.Audio;

/// <summary>
/// Captures one app and its child processes, whichever output they play on. The virtual
/// loopback device has no mix format, so a fixed one is requested. A silent app delivers no
/// packets, which is not an error. If capture stops on an error, such as the audio service
/// restarting after sleep, it starts again by itself.
/// </summary>
public sealed class ProcessLoopbackSource : IAudioSource
{
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LaterRetry = TimeSpan.FromSeconds(5);

    private readonly int rootPid;
    private readonly string displayName;
    private readonly ILogger<ProcessLoopbackSource> logger;
    private readonly Lock _gate = new();
    private readonly Timer _restart;
    private WasapiRecorder? _recorder;
    private volatile bool _disposed;
    private SampleFormat _sampleFormat;
    private float[] _samples = [];
    private bool _formatPending;

    public ProcessLoopbackSource(int rootPid, string displayName, ILogger<ProcessLoopbackSource> logger)
    {
        this.rootPid = rootPid;
        this.displayName = displayName;
        this.logger = logger;
        _restart = new Timer(_ => Restart());
    }

    public int RootPid => rootPid;
    public string DisplayName => displayName;
    public WaveFormatInfo Format { get; private set; } = new(48000, 2);

    public event Action<WaveFormatInfo>? FormatChanged;
    public event Action<ReadOnlyMemory<float>>? SamplesAvailable;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StartRecorder();
        }
    }

    private void StartRecorder()
    {
        if (_recorder != null) return;

        // Activation completes on a COM worker thread, so it must not wait on the app's STA thread.
        var recorder = Task.Run(() => new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)rootPid, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(Format.SampleRate, Format.Channels))
            .BuildAsync()).GetAwaiter().GetResult();

        _sampleFormat = WaveFormats.ToSampleFormat(recorder.WaveFormat);
        Format = new WaveFormatInfo(recorder.WaveFormat.SampleRate, recorder.WaveFormat.Channels);
        _formatPending = true;
        recorder.DataAvailable += OnDataAvailable;
        recorder.RecordingStopped += OnRecordingStopped;
        recorder.StartRecording();
        _recorder = recorder;
        logger.LogInformation("Capturing {Name}, process {Pid} and its children", displayName, rootPid);
    }

    public void Stop()
    {
        lock (_gate) StopRecorder();
    }

    private void StopRecorder()
    {
        if (_recorder == null) return;
        _recorder.DataAvailable -= OnDataAvailable;
        _recorder.RecordingStopped -= OnRecordingStopped;
        _recorder.Dispose();
        _recorder = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _restart.Dispose();
            StopRecorder();
        }
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (_formatPending)
        {
            _formatPending = false;
            FormatChanged?.Invoke(Format);
        }

        var count = buffer.Length / SampleConverter.BytesPerSample(_sampleFormat);
        if (_samples.Length < count) _samples = new float[count];

        if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(_samples, 0, count);
        else SampleConverter.ToFloat(buffer, _sampleFormat, _samples);

        SamplesAvailable?.Invoke(_samples.AsMemory(0, count));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception == null) return;
        logger.LogWarning(e.Exception, "Capture of {Name} stopped unexpectedly, starting it again", displayName);

        // No lock here: this runs on the capture thread, which Dispose may be waiting for.
        try
        {
            if (!_disposed) _restart.Change(FirstRetry, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposed meanwhile; nothing to restart.
        }
    }

    private void Restart()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                StopRecorder();
                StartRecorder();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not capture {Name} again, trying later", displayName);
                _restart.Change(LaterRetry, Timeout.InfiniteTimeSpan);
            }
        }
    }
}

internal static class WaveFormats
{
    public static SampleFormat ToSampleFormat(WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || format is WaveFormatExtensible { SubFormat: var sub } && sub == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;

        return (isFloat, format.BitsPerSample) switch
        {
            (true, 32) => SampleFormat.Float32,
            (false, 16) => SampleFormat.Pcm16,
            (false, 24) => SampleFormat.Pcm24,
            (false, 32) => SampleFormat.Pcm32,
            _ => throw new NotSupportedException($"Unsupported capture format: {format}"),
        };
    }
}
