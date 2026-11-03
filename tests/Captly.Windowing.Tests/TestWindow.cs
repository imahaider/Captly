using System.Runtime.InteropServices;

namespace Captly.Windowing.Tests;

/// <summary>
/// A real top-level window in this process, almost fully transparent so nothing shows. The process
/// is made Per-Monitor V2 aware first, as Captly is, so every coordinate is in physical pixels.
/// Create, use and dispose it on one thread.
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private const long WsPopup = 0x80000000;
    private const long WsVisible = 0x10000000;
    private const long WsExLayered = 0x00080000;
    private const uint LwaAlpha = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int SwShowMinNoActive = 7;
    private const int SwShowNoActivate = 4;
    private static readonly nint PerMonitorAwareV2 = -4;
    private static readonly Lazy<bool> Aware = new(() => SetProcessDpiAwarenessContext(PerMonitorAwareV2));

    public TestWindow(int left, int top, int width, int height, string? title = null)
    {
        _ = Aware.Value;
        Title = title ?? $"Captly test window {Guid.NewGuid():N}";
        Handle = CreateWindowEx(WsExLayered, "STATIC", Title, unchecked((uint)(WsPopup | WsVisible)), left, top, width, height, 0, 0, 0, 0);
        if (Handle == 0) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        SetLayeredWindowAttributes(Handle, 0, 1, LwaAlpha);
    }

    public nint Handle { get; }

    public string Title { get; }

    public void Move(int left, int top, int width, int height) => SetWindowPos(Handle, 0, left, top, width, height, SwpNoZOrder | SwpNoActivate);

    public void Minimise() => ShowWindow(Handle, SwShowMinNoActive);

    public void Restore() => ShowWindow(Handle, SwShowNoActivate);

    public void Dispose() => DestroyWindow(Handle);

    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern nint CreateWindowEx(long exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32")] private static extern bool SetProcessDpiAwarenessContext(nint context);
}
