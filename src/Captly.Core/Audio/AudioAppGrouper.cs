namespace Captly.Core.Audio;

/// <summary>One audio session as Windows reports it.</summary>
public readonly record struct SessionSample(int ProcessId, float Peak, bool IsActive);

/// <summary>What the Source screen shows for an app: its name and where its icon comes from.</summary>
public sealed record ProcessDetails(string DisplayName, string? ExePath);

/// <summary>Turns raw sessions into one entry per app. Pure, so it is easy to test.</summary>
public static class AudioAppGrouper
{
    /// <summary>
    /// Groups sessions under their root process, loudest first. Leaves out the system sounds
    /// session (PID 0) and Captly itself. When two entries share a name, such as two browser
    /// windows, each gets its window title as the subtitle so they can be told apart.
    /// </summary>
    public static List<AudioAppInfo> Group(
        IEnumerable<SessionSample> sessions,
        ProcessTree tree,
        int ownPid,
        Func<int, string, ProcessDetails> describe,
        Func<int, string?> windowTitle)
    {
        var apps = sessions
            .Where(s => s.ProcessId != 0)
            .GroupBy(s => tree.RootOf(s.ProcessId))
            .Where(g => g.Key != ownPid)
            .Select(g =>
            {
                var exe = tree.ExeName(g.Key) ?? $"PID {g.Key}";
                var details = describe(g.Key, exe);
                var peak = g.Max(s => s.IsActive ? s.Peak : 0);
                return new AudioAppInfo(g.Key, exe, details.DisplayName, details.ExePath, peak, null);
            })
            .OrderByDescending(a => a.PeakLevel)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var repeated = apps.GroupBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (var i = 0; i < apps.Count; i++)
        {
            if (!repeated.Contains(apps[i].DisplayName)) continue;
            var title = windowTitle(apps[i].RootPid);
            apps[i] = apps[i] with { Subtitle = string.IsNullOrWhiteSpace(title) ? $"Process {apps[i].RootPid}" : title };
        }
        return apps;
    }
}
