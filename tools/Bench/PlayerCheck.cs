using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Captly.Bench;

/// <summary>
/// A stand-in video window for testing pinned captions without touching the user's own apps.
/// Run `Captly --overlay-demo bench` beside it. The window moves, resizes, maximises, goes
/// fullscreen, minimises and comes back on a script, and every 250 ms this prints its own frame
/// and where Captly's caption overlay is.
/// </summary>
internal static class PlayerCheck
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const int GwlStyle = -16;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const int SwMaximize = 3;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const uint PmRemove = 1;
    private const int DwmwaExtendedFrameBounds = 9;
    private static readonly nint PerMonitorAwareV2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public override string ToString() => $"{Right - Left}x{Bottom - Top} at {Left},{Top}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    public static int Run()
    {
        SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        var hwnd = CreateWindowEx(0, "STATIC", "Captly test player - Inside a jet engine", WsOverlappedWindow, 200, 150, 1000, 600, 0, 0, 0, 0);

        // The first show takes the launcher's show flag, which may be "hidden", so show it twice.
        ShowWindow(hwnd, SwRestore);
        ShowWindow(hwnd, SwRestore);
        Process? cover = null;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor);

        (double At, string Name, Action Step)[] script =
        [
            (5, "move", () => SetWindowPos(hwnd, 0, 600, 300, 1000, 600, SwpNoZOrder)),
            (9, "resize", () => SetWindowPos(hwnd, 0, 600, 300, 700, 450, SwpNoZOrder)),
            (13, "maximise", () => ShowWindow(hwnd, SwMaximize)),
            (17, "fullscreen", () =>
            {
                ShowWindow(hwnd, SwRestore);
                SetWindowLongPtr(hwnd, GwlStyle, unchecked((nint)(WsPopup | WsVisible)));
                var m = monitor.Monitor;
                SetWindowPos(hwnd, 0, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, SwpNoZOrder | SwpFrameChanged);
            }),
            (21, "window", () =>
            {
                SetWindowLongPtr(hwnd, GwlStyle, (nint)(WsOverlappedWindow | WsVisible));
                SetWindowPos(hwnd, 0, 200, 150, 1000, 600, SwpNoZOrder | SwpFrameChanged);
            }),
            (25, "minimise", () => ShowWindow(hwnd, SwMinimize)),
            (29, "restore", () => ShowWindow(hwnd, SwRestore)),
            // Another app's window over the captions. It must be another process: a second window
            // of this one would simply become the window the captions follow.
            (33, "cover", () => cover = Process.Start("powershell", "-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; "
                + "$f = New-Object Windows.Forms.Form; $f.Text = 'Captly test cover'; $f.StartPosition = 'Manual'; "
                + "$f.SetBounds(360, 400, 400, 240); $t = New-Object Windows.Forms.Timer; $t.Interval = 4000; "
                + "$t.Add_Tick({ $f.Close() }); $t.Start(); [void]$f.ShowDialog()\"")),
        ];
        const double End = 41;

        var clock = Stopwatch.StartNew();
        var next = 0;
        var lastLog = -1.0;
        while (clock.Elapsed.TotalSeconds < End)
        {
            while (PeekMessage(out var msg, 0, 0, 0, PmRemove))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            var now = clock.Elapsed.TotalSeconds;
            if (next < script.Length && now >= script[next].At)
            {
                Console.WriteLine($"{now,5:F1}s  -> {script[next].Name}");
                script[next++].Step();
            }

            if (now - lastLog >= 0.25)
            {
                lastLog = now;
                DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var frame, Marshal.SizeOf<Rect>());
                var overlay = FindWindow(null, "Captly captions");
                var shown = overlay != 0 && IsWindowVisible(overlay);
                var where = "hidden";
                if (shown && GetWindowRect(overlay, out var o)) where = $"{o}  centre {(o.Left + o.Right) / 2}, bottom {o.Bottom}";
                var state = IsIconic(hwnd) ? "minimised" : !IsWindowVisible(hwnd) ? "invisible" : frame.ToString();
                Console.WriteLine($"{now,5:F1}s  player {state}  centre {(frame.Left + frame.Right) / 2}, bottom {frame.Bottom}  |  captions {where}");
            }
            Thread.Sleep(15);
        }

        DestroyWindow(hwnd);
        cover?.Dispose();
        return 0;
    }

    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")] private static extern nint FindWindow(string? className, string title);
    [DllImport("user32")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Msg msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Msg msg);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);
    [DllImport("user32")] private static extern bool SetProcessDpiAwarenessContext(nint context);
}
