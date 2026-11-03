using Captly.Core.Audio;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace Captly.Audio;

public sealed class AudioSourceFactory(ILoggerFactory loggers) : IAudioSourceFactory, IDisposable
{
    private readonly object _gate = new();
    private readonly Lazy<bool> _perProcess = new(() => ProbePerProcess(loggers.CreateLogger<AudioSourceFactory>()));
    private AudioSessionEnumerator? _sessions;
    private IReadOnlyList<AudioAppInfo> _lastApps = [];

    /// <summary>
    /// Asks Windows to activate process loopback on Captly itself. That works on any build that
    /// supports it, so there is no build number to keep in step (DECISIONS.md D9).
    /// </summary>
    public bool SupportsPerProcess => _perProcess.Value;

    public IReadOnlyList<AudioAppInfo> ListActiveApps()
    {
        lock (_gate)
        {
            _sessions ??= new AudioSessionEnumerator();
            return _lastApps = _sessions.List();
        }
    }

    public IAudioSource CreateForProcess(int rootPid)
    {
        var name = _lastApps.FirstOrDefault(a => a.RootPid == rootPid)?.DisplayName ?? $"process {rootPid}";
        return new ProcessLoopbackSource(rootPid, name, loggers.CreateLogger<ProcessLoopbackSource>());
    }

    public IAudioSource CreateSystemWide() => new SystemLoopbackSource(loggers.CreateLogger<SystemLoopbackSource>());

    public bool IsRunning(int pid) => Processes.IsRunning(pid);

    public void Dispose()
    {
        lock (_gate) _sessions?.Dispose();
    }

    private static bool ProbePerProcess(ILogger logger)
    {
        try
        {
            using var client = Task.Run(() => AudioClient.ActivateProcessLoopbackAsync((uint)Environment.ProcessId, ProcessLoopbackMode.IncludeTargetProcessTree))
                .GetAwaiter().GetResult();
            logger.LogInformation("Per-app capture is available");
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Per-app capture is not available. Using all system audio");
            return false;
        }
    }
}
