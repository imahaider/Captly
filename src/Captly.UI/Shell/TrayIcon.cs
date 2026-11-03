using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Win32;

namespace Captly.UI.Shell;

public enum TrayState { On, Waiting, Off }

/// <summary>One entry of the tray's right-click menu. A null text draws a separator.</summary>
public sealed record TrayMenuItem(string? Text, Action? Click = null, bool Checked = false);

/// <summary>
/// The tray icon (PLAN.md §6.3 #9): three states, drawn for a light or dark taskbar from the brand
/// kit (D27). Left click raises <see cref="Clicked"/>. Right click shows a native Windows menu.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly TaskbarIcon _icon;
    private readonly Func<IReadOnlyList<TrayMenuItem>> _menu;
    private readonly nint _menuOwner;
    private TrayState _state = TrayState.Waiting;

    /// <param name="menu">Builds the right-click menu when it opens, so it shows the current state.</param>
    /// <param name="menuOwner">A window of this process for the menu, as Windows requires.</param>
    public TrayIcon(Func<IReadOnlyList<TrayMenuItem>> menu, nint menuOwner)
    {
        _menu = menu;
        _menuOwner = menuOwner;
        _icon = new TaskbarIcon { NoLeftClickDelay = true, ToolTipText = "Captly" };
        _icon.TrayLeftMouseUp += (_, _) => Clicked?.Invoke();
        _icon.TrayRightMouseUp += (_, _) => ShowMenu();
        UpdateIcon();
        // Efficiency mode would throttle the whole process, audio included, so it stays off.
        _icon.ForceCreate(false);
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    public event Action? Clicked;

    public TrayState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            UpdateIcon();
        }
    }

    /// <summary>"Captly — Inside a jet engine".</summary>
    public string ToolTip
    {
        get => _icon.ToolTipText ?? "";
        set => _icon.ToolTipText = value;
    }

    /// <summary>A Windows notification from the tray icon, such as "Finish setting up Captly".</summary>
    public void Notify(string title, string message) => _icon.ShowNotification(title, message);

    /// <summary>The light-taskbar icons have a dark glyph, and the dark ones a light glyph.</summary>
    public static string IconName(TrayState state, bool lightTaskbar) =>
        $"CaptlyTray-{state.ToString().ToLowerInvariant()}-{(lightTaskbar ? "light" : "dark")}-taskbar.ico";

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _icon.Dispose();
    }

    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
    }

    private void UpdateIcon()
    {
        var uri = new Uri($"pack://application:,,,/Captly.UI;component/Tray/{IconName(_state, TaskbarIsLight())}");
        _icon.IconSource = BitmapFrame.Create(uri);
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) _icon.Dispatcher.BeginInvoke(UpdateIcon);
    }

    private void ShowMenu()
    {
        var menu = new PopupMenu();
        foreach (var item in _menu())
        {
            if (item.Text == null)
            {
                menu.Items.Add(new PopupMenuSeparator());
                continue;
            }
            var entry = new PopupMenuItem(item.Text, (_, _) => item.Click?.Invoke()) { Checked = item.Checked };
            menu.Items.Add(entry);
        }

        GetCursorPos(out var cursor);
        SetForegroundWindow(_menuOwner);
        menu.Show(_menuOwner, cursor.X, cursor.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] private static extern bool SetForegroundWindow(nint hwnd);
}
