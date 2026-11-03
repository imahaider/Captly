using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Captly.Core.Audio;

namespace Captly.Audio;

/// <summary>Win32 process lookups that need no special rights.</summary>
internal static class Processes
{
    private const int SnapProcess = 0x2;
    private const uint QueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;

    /// <summary>Every process and its parent, from one Toolhelp snapshot.</summary>
    public static ProcessTree Snapshot()
    {
        var entries = new List<ProcessEntry>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1) return new ProcessTree(entries);
        try
        {
            var entry = new ProcessEntry32 { Size = Marshal.SizeOf<ProcessEntry32>() };
            for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
                entries.Add(new ProcessEntry(entry.ProcessId, entry.ParentProcessId, entry.ExeFile));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return new ProcessTree(entries);
    }

    public static bool IsRunning(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle == 0) return false;
        try
        {
            return GetExitCodeProcess(handle, out var code) && code == StillActive;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static string? ExePath(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle == 0) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The exe's own description, such as "Google Chrome", or its file name.</summary>
    public static ProcessDetails Describe(int pid, string exeName)
    {
        var path = ExePath(pid);
        string? description = null;
        try
        {
            if (path != null) description = FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch (FileNotFoundException)
        {
            // The exe went away; the file name will do.
        }
        var name = string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(exeName) : description.Trim();
        return new ProcessDetails(name, path);
    }

    public static string? WindowTitle(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainWindowTitle;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public int Size;
        public int Usage;
        public int ProcessId;
        public nint DefaultHeapId;
        public int ModuleId;
        public int Threads;
        public int ParentProcessId;
        public int BasePriority;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(int flags, int pid);
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32")] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32")] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")] private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
}
