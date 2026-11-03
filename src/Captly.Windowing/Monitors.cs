using System.Runtime.InteropServices;
using Captly.Core.Windowing;

namespace Captly.Windowing;

/// <summary>Monitor bounds and DPI. Captly is Per-Monitor V2 aware, so all of it is in physical pixels.</summary>
public static class Monitors
{
    private const double BaseDpi = 96;

    public static MonitorInfo Primary() => Describe(Native.MonitorFromPoint(default, Native.MonitorDefaultToPrimary));

    /// <summary>The monitor under this point, or the nearest one if it is off every screen.</summary>
    public static MonitorInfo FromPoint(PixelPoint point) =>
        Describe(Native.MonitorFromPoint(new Native.Point { X = point.X, Y = point.Y }, Native.MonitorDefaultToNearest));

    public static MonitorInfo ForWindow(nint hwnd) => Describe(Native.MonitorFromWindow(hwnd, Native.MonitorDefaultToNearest));

    private static MonitorInfo Describe(nint monitor)
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.GetMonitorInfo(monitor, ref info);
        var scale = Native.GetDpiForMonitor(monitor, Native.MdtEffectiveDpi, out var dpi, out _) == 0 ? dpi / BaseDpi : 1;
        return new MonitorInfo(ToPixels(info.Monitor), ToPixels(info.Work), scale);
    }

    internal static PixelRect ToPixels(Native.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
