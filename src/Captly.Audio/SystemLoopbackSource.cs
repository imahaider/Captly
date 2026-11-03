using Captly.Core.Audio;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Captly.Audio;

/// <summary>
/// Captures everything the default speakers play. Follows the default device: when it changes or
/// fails, capture reopens on the new device, possibly with a new format. With no output device,
/// it keeps retrying rather than failing.
/// </summary>
public sealed class SystemLoopbackSource : IAudioSource
{
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly ILogger<SystemLoopbackSource> _logger;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly MMDeviceNotificationClient _notifications;
    private readonly Timer _reopenTimer;
    private readonly object _gate = new();
    private WasapiRecorder? _recorder;
    private SampleFormat _sampleFormat;
    private float[] _samples = [];
    private volatile bool _running;
    private bool _formatPending;

    public SystemLoopbackSource(ILogger<SystemLoopbackSource> logger)
    {
        _logger = logger;
        _reopenTimer = new Timer(_ => Reopen());
        _notifications = _devices.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
    }

    public string DisplayName => "All system audio";
    public WaveFormatInfo Format { get; private set; }

    public event Action<WaveFormatInfo>? FormatChanged;
    public event Action<ReadOnlyMemory<float>>? SamplesAvailable;

    public void Start()
    {
        if (_running) return;
        _running = true;

        // Keeps NAudio's COM objects off the app's STA thread.
        Task.Run(Reopen).GetAwaiter().GetResult();
    }

    public void Stop()
    {
        _running = false;
        _reopenTimer.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_gate) Close();
    }

    public void Dispose()
    {
        Stop();
        _notifications.Dispose();
        _reopenTimer.Dispose();
        _devices.Dispose();
    }

    private void Open()
    {
        lock (_gate)
        {
            if (!_running) return;
            Close();

            var recorder = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
            var format = recorder.WaveFormat;
            _sampleFormat = WaveFormats.ToSampleFormat(format);
            Format = new WaveFormatInfo(format.SampleRate, format.Channels);
            _formatPending = true;

            recorder.DataAvailable += OnDataAvailable;
            recorder.RecordingStopped += OnRecordingStopped;
            recorder.StartRecording();
            _recorder = recorder;
            _logger.LogInformation("System capture started on {Device}: {Rate} Hz, {Channels} channels, {Format}",
                recorder.DeviceFriendlyName, format.SampleRate, format.Channels, _sampleFormat);
        }
    }

    private void Close()
    {
        if (_recorder == null) return;
        _recorder.DataAvailable -= OnDataAvailable;
        _recorder.RecordingStopped -= OnRecordingStopped;
        _recorder.Dispose();
        _recorder = null;
    }

    private void Reopen()
    {
        try
        {
            Open();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "System capture could not open. Retrying");
            ScheduleReopen(RetryDelay);
        }
    }

    // Takes no lock: Close holds the gate while it waits for the capture thread that may call this.
    private void ScheduleReopen(TimeSpan delay)
    {
        if (_running) _reopenTimer.Change(delay, Timeout.InfiniteTimeSpan);
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
        _logger.LogWarning(e.Exception, "System capture stopped unexpectedly");
        ScheduleReopen(ReopenDelay);
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        // Fires once per role, on a thread that must not block. The timer collapses them.
        if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia) ScheduleReopen(ReopenDelay);
    }
}
