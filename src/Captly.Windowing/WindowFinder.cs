using System.Runtime.InteropServices;
using System.Text;
using Captly.Core.Windowing;

namespace Captly.Windowing;

/// <summary>Reads top-level windows and their placement from Win32.</summary>
public static class WindowFinder
{
    // Store apps draw in a CoreWindow of their own process, hosted by ApplicationFrameHost.
    private const string FrameClass = "ApplicationFrameWindow";
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    // A guard against a z-order that changes while it is walked.
    private const int MaxWindowsAbove = 4096;

    /// <summary>Top-level windows of the processes this accepts, top of the z-order first.</summary>
    public static List<WindowInfo> List(Func<int, bool> includePid)
    {
        var foreground = Native.GetForegroundWindow();
        var windows = new List<WindowInfo>();
        Native.EnumWindows((hwnd, _) =>
        {
            var pid = ProcessOf(hwnd);
            if (includePid(pid)) windows.Add(Describe(hwnd, pid, foreground));
            return true;
        }, 0);
        return windows;
    }

    /// <summary>
    /// The window's placement now. Windows above it that could cover the captions are listed,
    /// leaving out see-through overlays, tool windows, its own pop-ups and the excluded process.
    /// </summary>
    public static WindowPlacement Placement(nint hwnd, int excludedPid)
    {
        var minimised = Native.IsIconic(hwnd);
        var hidden = !Native.IsWindowVisible(hwnd) || IsCloaked(hwnd);

        // Captions are hidden anyway while the window is, so the z-order walk is skipped.
        var above = new List<PixelRect>();
        var window = minimised || hidden ? 0 : Native.GetWindow(hwnd, Native.GwHwndPrev);
        for (var i = 0; window != 0 && i < MaxWindowsAbove; i++, window = Native.GetWindow(window, Native.GwHwndPrev))
        {
            if (CanCover(window, hwnd, excludedPid)) above.Add(Bounds(window));
        }

        return new WindowPlacement(hwnd, Bounds(hwnd), Monitors.ForWindow(hwnd))
        {
            IsMinimised = minimised,
            IsHidden = hidden,
            Above = above,
        };
    }

    public static bool Exists(nint hwnd) => Native.IsWindow(hwnd);

    public static string ClassName(nint hwnd)
    {
        var name = new StringBuilder(256);
        Native.GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    /// <summary>The process that draws the window. For a Store app's frame, that is the app inside it.</summary>
    public static int ProcessOf(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (ClassName(hwnd) != FrameClass) return pid;

        var inner = pid;
        Native.EnumChildWindows(hwnd, (child, _) =>
        {
            if (ClassName(child) != CoreWindowClass) return true;
            Native.GetWindowThreadProcessId(child, out inner);
            return false;
        }, 0);
        return inner;
    }

    private static WindowInfo Describe(nint hwnd, int pid, nint foreground)
    {
        var styles = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle);
        return new WindowInfo(hwnd, pid, Title(hwnd), Bounds(hwnd))
        {
            IsVisible = Native.IsWindowVisible(hwnd),
            IsCloaked = IsCloaked(hwnd),
            IsMinimised = Native.IsIconic(hwnd),
            IsToolWindow = (styles & Native.WsExToolWindow) != 0,
            HasOwner = Native.GetWindow(hwnd, Native.GwOwner) != 0,
            IsForeground = hwnd == foreground,
        };
    }

    private static bool CanCover(nint window, nint target, int excludedPid)
    {
        if (!Native.IsWindowVisible(window) || Native.IsIconic(window)) return false;
        var styles = Native.GetWindowLongPtr(window, Native.GwlExStyle);
        if ((styles & (Native.WsExTransparent | Native.WsExToolWindow | Native.WsExNoActivate)) != 0) return false;
        if (Native.GetWindow(window, Native.GwOwner) == target || IsCloaked(window)) return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        return pid != excludedPid && !Bounds(window).IsEmpty;
    }

    private static string Title(nint hwnd)
    {
        var length = Native.GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var title = new StringBuilder(length + 1);
        Native.GetWindowText(hwnd, title, title.Capacity);
        return title.ToString();
    }

    // The frame DWM draws, without the invisible resize borders that GetWindowRect includes.
    private static PixelRect Bounds(nint hwnd)
    {
        if (Native.DwmGetWindowAttribute(hwnd, Native.DwmwaExtendedFrameBounds, out Native.Rect rect, Marshal.SizeOf<Native.Rect>()) != 0)
            Native.GetWindowRect(hwnd, out rect);
        return Monitors.ToPixels(rect);
    }

    private static bool IsCloaked(nint hwnd) =>
        Native.DwmGetWindowAttribute(hwnd, Native.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
}
