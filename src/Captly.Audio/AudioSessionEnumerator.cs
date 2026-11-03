using System.Runtime.InteropServices;
using Captly.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Captly.Audio;

/// <summary>
/// Lists the apps with audio sessions on any active output. Built for ten polls a second: the
/// output list and process tree are reused until they go stale, and app names are cached.
/// </summary>
internal sealed class AudioSessionEnumerator : IDisposable
{
    private static readonly TimeSpan EndpointLifetime = TimeSpan.FromSeconds(2);

    private readonly MMDeviceEnumerator _devices = new();
    private readonly Dictionary<(int Pid, string Exe), ProcessDetails> _names = [];
    private List<MMDevice> _endpoints = [];
    private DateTime _endpointsAt;
    private ProcessTree _tree = new([]);

    public List<AudioAppInfo> List()
    {
        var sessions = Sample();
        if (sessions.Any(s => s.ProcessId != 0 && !_tree.Contains(s.ProcessId))) _tree = Processes.Snapshot();

        return AudioAppGrouper.Group(sessions, _tree, Environment.ProcessId, Describe, Processes.WindowTitle);
    }

    public void Dispose()
    {
        foreach (var endpoint in _endpoints) endpoint.Dispose();
        _devices.Dispose();
    }

    private ProcessDetails Describe(int pid, string exe)
    {
        if (!_names.TryGetValue((pid, exe), out var details)) _names[(pid, exe)] = details = Processes.Describe(pid, exe);
        return details;
    }

    private List<SessionSample> Sample()
    {
        if (DateTime.UtcNow - _endpointsAt > EndpointLifetime)
        {
            foreach (var endpoint in _endpoints) endpoint.Dispose();
            _endpoints = _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            _endpointsAt = DateTime.UtcNow;
        }

        var samples = new List<SessionSample>();
        foreach (var endpoint in _endpoints)
        {
            try
            {
                var manager = endpoint.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.IsSystemSoundsSession) continue;
                    var state = session.State;
                    if (state == AudioSessionState.AudioSessionStateExpired) continue;
                    var active = state == AudioSessionState.AudioSessionStateActive;
                    samples.Add(new SessionSample((int)session.GetProcessID, active ? session.AudioMeterInformation.MasterPeakValue : 0, active));
                }
            }
            catch (COMException)
            {
                // The output went away mid-poll. Look again next time.
                _endpointsAt = default;
            }
        }
        return samples;
    }
}
