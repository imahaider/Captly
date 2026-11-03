using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Captly.Bench;

/// <summary>
/// A stand-in video window for screenshots: shows a bitmap in a titled window for a while.
/// Run `Captly --overlay 40 samples\two_cities_16k.wav on=bench` beside it, and Captly captions
/// the sample on this window as it would on a real player.
/// </summary>
internal static class StageCheck
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsExTopmost = 0x00000008;
    private const uint SsBitmap = 0x0E;
    private const uint SsRealSizeControl = 0x40;
    private const uint StmSetImage = 0x0172;
    private const uint LrLoadFromFile = 0x10;
    private const int SwRestore = 9;
    private const uint PmRemove = 1;
    private static readonly nint PerMonitorAwareV2 = -4;

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
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    public static int Run(string bitmap, string title, int seconds)
    {
        SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        var image = LoadImage(0, Path.GetFullPath(bitmap), 0, 0, 0, LrLoadFromFile);
        if (image == 0)
        {
            Console.WriteLine($"Could not load {bitmap}. It must be a .bmp.");
            return 1;
        }

        // Topmost, so no other window covers it, which would hide the captions (D37).
        var hwnd = CreateWindowEx(WsExTopmost, "STATIC", title, WsOverlappedWindow | SsBitmap | SsRealSizeControl, 100, 100, 1280, 760, 0, 0, 0, 0);
        SendMessage(hwnd, StmSetImage, 0, image);
        ShowWindow(hwnd, SwRestore);
        ShowWindow(hwnd, SwRestore);
        DwmGetWindowAttribute(hwnd, 9, out var frame, Marshal.SizeOf<Rect>());
        Console.WriteLine($"Stage at {frame.Left},{frame.Top} to {frame.Right},{frame.Bottom}");

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            while (PeekMessage(out var msg, 0, 0, 0, PmRemove))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            Thread.Sleep(15);
        }
        DestroyWindow(hwnd);
        return 0;
    }

    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")] private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Msg msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Msg msg);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);
    [DllImport("user32")] private static extern bool SetProcessDpiAwarenessContext(nint context);
}
