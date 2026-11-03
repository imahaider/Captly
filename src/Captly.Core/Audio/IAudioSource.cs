namespace Captly.Core.Audio;

/// <summary>Format of the interleaved 32-bit float samples a source delivers.</summary>
public readonly record struct WaveFormatInfo(int SampleRate, int Channels);

public interface IAudioSource : IDisposable
{
    string DisplayName { get; }
    WaveFormatInfo Format { get; }

    /// <summary>
    /// Raised on the capture thread before the first samples of each new stream, so a device
    /// change can alter the format without a lock.
    /// </summary>
    event Action<WaveFormatInfo> FormatChanged;

    /// <summary>
    /// Raised on the capture thread. The memory is only valid during the call. Sources may go
    /// quiet for as long as nothing is playing, which is not an error.
    /// </summary>
    event Action<ReadOnlyMemory<float>> SamplesAvailable;

    void Start();
    void Stop();
}

public interface IAudioSourceFactory
{
    /// <summary>False on Windows builds without process loopback.</summary>
    bool SupportsPerProcess { get; }

    /// <summary>Apps making sound now, for the Source screen and popover.</summary>
    IReadOnlyList<AudioAppInfo> ListActiveApps();

    /// <summary>Captures the process and its child process tree.</summary>
    IAudioSource CreateForProcess(int rootPid);

    IAudioSource CreateSystemWide();

    /// <summary>True while the process exists, so a quiet target is not mistaken for a closed one.</summary>
    bool IsRunning(int pid);
}

public record AudioAppInfo(
    int RootPid,
    string ExeName,
    string DisplayName,
    string? IconPath,
    float PeakLevel,
    string? Subtitle);
