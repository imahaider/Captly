using Captly.Core.Settings;

namespace Captly.Core.Shell;

public enum HotkeyAction { Toggle, Hide, NextSource, OpenTranscript, OpenSettings }

/// <summary>The global shortcuts of PLAN.md §6.3 #7, their labels, and where each lives in the settings.</summary>
public static class Hotkeys
{
    public static IReadOnlyList<(HotkeyAction Action, string Label)> All { get; } =
    [
        (HotkeyAction.Toggle, "Turn captions on or off"),
        (HotkeyAction.Hide, "Show or hide captions"),
        (HotkeyAction.NextSource, "Switch to the next app playing sound"),
        (HotkeyAction.OpenTranscript, "Open the transcript of this video"),
        (HotkeyAction.OpenSettings, "Open Captly settings"),
    ];

    public static string Label(HotkeyAction action) => All.First(a => a.Action == action).Label;

    public static string Get(HotkeySettings keys, HotkeyAction action) => action switch
    {
        HotkeyAction.Toggle => keys.Toggle,
        HotkeyAction.Hide => keys.Hide,
        HotkeyAction.NextSource => keys.NextSource,
        HotkeyAction.OpenTranscript => keys.OpenTranscript,
        HotkeyAction.OpenSettings => keys.OpenSettings,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static HotkeySettings With(HotkeySettings keys, HotkeyAction action, string shortcut) => action switch
    {
        HotkeyAction.Toggle => keys with { Toggle = shortcut },
        HotkeyAction.Hide => keys with { Hide = shortcut },
        HotkeyAction.NextSource => keys with { NextSource = shortcut },
        HotkeyAction.OpenTranscript => keys with { OpenTranscript = shortcut },
        HotkeyAction.OpenSettings => keys with { OpenSettings = shortcut },
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>The other action already using this shortcut, if any. Case and spacing are ignored.</summary>
    public static HotkeyAction? UsedBy(HotkeySettings keys, HotkeyAction action, string shortcut) =>
        All.Select(a => a.Action).Where(other => other != action && Same(Get(keys, other), shortcut)).Cast<HotkeyAction?>().FirstOrDefault();

    /// <summary>The recorder's message for a clash with another Captly shortcut.</summary>
    public static string? Conflict(HotkeySettings keys, HotkeyAction action, string shortcut) =>
        UsedBy(keys, action, shortcut) is { } other ? $"Already used to {char.ToLowerInvariant(Label(other)[0])}{Label(other)[1..]}." : null;

    /// <summary>How a shortcut reads in text: "Ctrl + Alt + H".</summary>
    public static string Display(string shortcut) => string.Join(" + ", shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Win32 modifier flags and virtual-key code for RegisterHotKey. Null if the text is not a usable shortcut.</summary>
    public static (uint Modifiers, uint Key)? Parse(string? shortcut)
    {
        const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;
        if (string.IsNullOrWhiteSpace(shortcut)) return null;

        uint modifiers = 0;
        uint? key = null;
        foreach (var part in shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= Control; break;
                case "ALT": modifiers |= Alt; break;
                case "SHIFT": modifiers |= Shift; break;
                case "WIN": modifiers |= Win; break;
                default:
                    if (key != null) return null;
                    key = KeyCode(part.ToUpperInvariant());
                    if (key == null) return null;
                    break;
            }
        }
        return key is { } k ? (modifiers, k) : null;
    }

    private static uint? KeyCode(string name) => name switch
    {
        { Length: 1 } when name[0] is >= 'A' and <= 'Z' or >= '0' and <= '9' => name[0],
        ['N', 'U', 'M', var d] when d is >= '0' and <= '9' => 0x60u + (uint)(d - '0'),
        ['F', ..] when int.TryParse(name[1..], out var f) && f is >= 1 and <= 24 => 0x70u + (uint)(f - 1),
        "SPACE" => 0x20,
        "PAGEUP" => 0x21,
        "PAGEDOWN" => 0x22,
        "END" => 0x23,
        "HOME" => 0x24,
        "LEFT" => 0x25,
        "UP" => 0x26,
        "RIGHT" => 0x27,
        "DOWN" => 0x28,
        "INSERT" => 0x2D,
        "DELETE" => 0x2E,
        _ => null,
    };

    private static bool Same(string a, string b) =>
        string.Equals(a.Replace(" ", ""), b.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
}
