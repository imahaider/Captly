namespace Captly.Core.Audio;

public readonly record struct ProcessEntry(int Pid, int ParentPid, string ExeName);

/// <summary>
/// A snapshot of which process started which. Apps such as browsers play sound from a child
/// process, so a session's PID is walked up to the app's root before capture.
/// </summary>
public sealed class ProcessTree
{
    private readonly Dictionary<int, ProcessEntry> _entries;

    public ProcessTree(IEnumerable<ProcessEntry> entries) => _entries = entries.ToDictionary(e => e.Pid);

    public bool Contains(int pid) => _entries.ContainsKey(pid);

    public string? ExeName(int pid) => _entries.TryGetValue(pid, out var entry) ? entry.ExeName : null;

    /// <summary>
    /// Walks up while the parent runs the same exe. Stops at a missing parent, and at a loop, which
    /// a reused PID can create. An unknown PID is its own root.
    /// </summary>
    public int RootOf(int pid)
    {
        var visited = new HashSet<int>();
        var current = pid;
        while (visited.Add(current)
            && _entries.TryGetValue(current, out var entry)
            && _entries.TryGetValue(entry.ParentPid, out var parent)
            && entry.ParentPid != current
            && string.Equals(parent.ExeName, entry.ExeName, StringComparison.OrdinalIgnoreCase))
        {
            current = parent.Pid;
        }
        return current;
    }
}
