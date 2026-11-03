using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Captly.UI.Controls;

/// <summary>Formats shortcuts as settings store them: "Ctrl+Alt+C".</summary>
public static class ShortcutKeys
{
    public static string? Compose(ModifierKeys modifiers, Key key)
    {
        var name = KeyName(key);
        if (name == null) return null;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        return string.Join('+', parts);
    }

    /// <summary>A global shortcut needs Ctrl, Alt or Win, or it would swallow normal typing. F keys may stand alone.</summary>
    public static bool IsUsable(ModifierKeys modifiers, Key key) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 || key is >= Key.F1 and <= Key.F24;

    public static IReadOnlyList<string> Split(string? shortcut) =>
        string.IsNullOrEmpty(shortcut) ? [] : shortcut.Split('+');

    /// <summary>Null for keys that cannot end a shortcut, such as a modifier held alone.</summary>
    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space",
        Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Insert or Key.Delete => key.ToString(),
        _ => null,
    };
}

/// <summary>
/// Keycaps showing a shortcut. Activating it asks for new keys, Esc cancels, and a conflict shows
/// inline. As a Button it is reachable by keyboard and exposes the Invoke pattern.
/// </summary>
public sealed class ShortcutRecorder : Button
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut), typeof(string), typeof(ShortcutRecorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((ShortcutRecorder)d).SetValue(KeycapsKey, ShortcutKeys.Split((string?)e.NewValue))));

    private static readonly DependencyPropertyKey KeycapsKey = DependencyProperty.RegisterReadOnly(
        nameof(Keycaps), typeof(IReadOnlyList<string>), typeof(ShortcutRecorder), new PropertyMetadata(Array.Empty<string>()));

    public static readonly DependencyProperty KeycapsProperty = KeycapsKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsRecordingKey = DependencyProperty.RegisterReadOnly(
        nameof(IsRecording), typeof(bool), typeof(ShortcutRecorder), new PropertyMetadata(false));

    public static readonly DependencyProperty IsRecordingProperty = IsRecordingKey.DependencyProperty;

    private static readonly DependencyPropertyKey MessageKey = DependencyProperty.RegisterReadOnly(
        nameof(Message), typeof(string), typeof(ShortcutRecorder), new PropertyMetadata(null));

    public static readonly DependencyProperty MessageProperty = MessageKey.DependencyProperty;

    static ShortcutRecorder() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ShortcutRecorder), new FrameworkPropertyMetadata(typeof(ShortcutRecorder)));

    public string? Shortcut
    {
        get => (string?)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public IReadOnlyList<string> Keycaps => (IReadOnlyList<string>)GetValue(KeycapsProperty);
    public bool IsRecording => (bool)GetValue(IsRecordingProperty);
    public string? Message => (string?)GetValue(MessageProperty);

    /// <summary>Returns a conflict message for a candidate shortcut, or null if it is free.</summary>
    public Func<string, string?>? FindConflict { get; set; }

    protected override void OnClick()
    {
        base.OnClick();
        if (!IsRecording) BeginRecording();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsRecording) return;
        Capture(Keyboard.Modifiers, e.Key == Key.System ? e.SystemKey : e.Key);
        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        EndRecording();
    }

    internal void BeginRecording()
    {
        SetValue(MessageKey, null);
        SetValue(IsRecordingKey, true);
    }

    internal void Capture(ModifierKeys modifiers, Key key)
    {
        if (key == Key.Escape)
        {
            EndRecording();
            return;
        }

        var candidate = ShortcutKeys.Compose(modifiers, key);
        if (candidate == null) return;

        if (!ShortcutKeys.IsUsable(modifiers, key))
        {
            SetValue(MessageKey, "Add Ctrl or Alt to this key.");
            return;
        }

        var conflict = FindConflict?.Invoke(candidate);
        if (conflict != null)
        {
            SetValue(MessageKey, conflict);
            return;
        }

        Shortcut = candidate;
        EndRecording();
    }

    private void EndRecording()
    {
        SetValue(IsRecordingKey, false);
        SetValue(MessageKey, null);
    }
}
