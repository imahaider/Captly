using System.Runtime.InteropServices;
using System.Text;

namespace Captly.Windowing;

internal static class Native
{
    public const int GwlExStyle = -20;
    public const long WsExTopmost = 0x00000008;
    public const long WsExTransparent = 0x00000020;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExNoActivate = 0x08000000;

    public const uint GwHwndPrev = 3;
    public const uint GwOwner = 4;

    public const int DwmwaExtendedFrameBounds = 9;
    public const int DwmwaCloaked = 14;

    public const uint MonitorDefaultToPrimary = 1;
    public const uint MonitorDefaultToNearest = 2;
    public const int MdtEffectiveDpi = 0;

    public const uint EventSystemForeground = 0x0003;
    public const uint EventSystemMoveSizeStart = 0x000A;
    public const uint EventSystemMoveSizeEnd = 0x000B;
    public const uint EventSystemMinimizeStart = 0x0016;
    public const uint EventSystemMinimizeEnd = 0x0017;
    public const uint EventObjectDestroy = 0x8001;
    public const uint EventObjectHide = 0x8003;
    public const uint EventObjectLocationChange = 0x800B;
    public const uint EventObjectNameChange = 0x800C;
    public const uint EventObjectCloaked = 0x8017;
    public const uint EventObjectUncloaked = 0x8018;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint WinEventSkipOwnProcess = 0x0002;
    public const int ObjIdWindow = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32")] public static extern bool EnumChildWindows(nint parent, EnumWindowsProc proc, nint lParam);
    [DllImport("user32")] public static extern int GetWindowThreadProcessId(nint hwnd, out int pid);
    [DllImport("user32")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] public static extern nint GetForegroundWindow();
    [DllImport("user32")] public static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] public static extern long GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32", EntryPoint = "GetWindowTextLengthW")] public static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] public static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] public static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32")] public static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32")] public static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32", EntryPoint = "GetMonitorInfoW")] public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, int pid, int thread, uint flags);
    [DllImport("user32")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("shcore")] public static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}
