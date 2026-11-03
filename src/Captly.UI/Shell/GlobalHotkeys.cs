using System.Runtime.InteropServices;
using System.Windows.Interop;
using Captly.Core.Settings;
using Captly.Core.Shell;

namespace Captly.UI.Shell;

/// <summary>
/// System-wide shortcuts through RegisterHotKey (PLAN.md Phase 8 step 4). They reach Captly even
/// over a fullscreen video. A message-only window receives them on the UI thread.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static readonly nint MessageOnly = -3;

    private readonly HwndSource _window;
    private readonly HashSet<int> _registered = [];

    public GlobalHotkeys()
    {
        _window = new HwndSource(new HwndSourceParameters("Captly hotkeys") { ParentWindow = MessageOnly, WindowStyle = 0 });
        _window.AddHook(OnMessage);
    }

    /// <summary>A window that never shows, which also owns the tray menu.</summary>
    public nint Handle => _window.Handle;

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Registers every shortcut afresh. Returns those Windows refused, because another app holds them.</summary>
    public IReadOnlyList<HotkeyAction> Apply(HotkeySettings keys)
    {
        foreach (var id in _registered) UnregisterHotKey(Handle, id);
        _registered.Clear();

        var refused = new List<HotkeyAction>();
        foreach (var (action, _) in Hotkeys.All)
        {
            var id = (int)action + 1;
            if (Hotkeys.Parse(Hotkeys.Get(keys, action)) is { } parsed && RegisterHotKey(Handle, id, parsed.Modifiers | ModNoRepeat, parsed.Key)) _registered.Add(id);
            else refused.Add(action);
        }
        return refused;
    }

    public void Dispose()
    {
        foreach (var id in _registered) UnregisterHotKey(Handle, id);
        _registered.Clear();
        _window.Dispose();
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmHotkey || !_registered.Contains((int)wParam)) return 0;
        handled = true;
        Pressed?.Invoke((HotkeyAction)((int)wParam - 1));
        return 0;
    }

    [DllImport("user32", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
