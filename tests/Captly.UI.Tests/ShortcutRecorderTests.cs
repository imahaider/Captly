using System.Windows.Input;
using Captly.UI.Controls;

namespace Captly.UI.Tests;

public class ShortcutRecorderTests
{
    private const ModifierKeys CtrlAlt = ModifierKeys.Control | ModifierKeys.Alt;

    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, Key.C, "Ctrl+Alt+C")]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.D5, "Ctrl+Shift+5")]
    [InlineData(ModifierKeys.Windows | ModifierKeys.Alt, Key.F9, "Alt+Win+F9")]
    [InlineData(ModifierKeys.None, Key.F12, "F12")]
    [InlineData(ModifierKeys.Control, Key.NumPad3, "Ctrl+Num3")]
    [InlineData(ModifierKeys.Alt, Key.Space, "Alt+Space")]
    public void Compose_UsesTheSettingsFormat(ModifierKeys modifiers, Key key, string expected) =>
        Assert.Equal(expected, ShortcutKeys.Compose(modifiers, key));

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.LWin)]
    [InlineData(Key.Tab)]
    public void Compose_RejectsKeysThatCannotEndAShortcut(Key key) => Assert.Null(ShortcutKeys.Compose(ModifierKeys.Control, key));

    [Theory]
    [InlineData(ModifierKeys.Control, Key.C, true)]
    [InlineData(ModifierKeys.Alt, Key.C, true)]
    [InlineData(ModifierKeys.Shift, Key.C, false)]
    [InlineData(ModifierKeys.None, Key.C, false)]
    [InlineData(ModifierKeys.None, Key.F5, true)]
    public void IsUsable_ProtectsNormalTyping(ModifierKeys modifiers, Key key, bool expected) =>
        Assert.Equal(expected, ShortcutKeys.IsUsable(modifiers, key));

    [Fact]
    public void Split_GivesOneKeycapPerKey()
    {
        Assert.Equal(["Ctrl", "Alt", "C"], ShortcutKeys.Split("Ctrl+Alt+C"));
        Assert.Empty(ShortcutKeys.Split(null));
    }

    [Fact]
    public void Capture_SavesTheNewKeysAndStops() => Ui.Run(() =>
    {
        var recorder = new ShortcutRecorder { Shortcut = "Ctrl+Alt+C" };
        recorder.BeginRecording();

        recorder.Capture(CtrlAlt, Key.K);

        Assert.Equal("Ctrl+Alt+K", recorder.Shortcut);
        Assert.Equal(["Ctrl", "Alt", "K"], recorder.Keycaps);
        Assert.False(recorder.IsRecording);
    });

    [Fact]
    public void Escape_CancelsAndKeepsTheOldKeys() => Ui.Run(() =>
    {
        var recorder = new ShortcutRecorder { Shortcut = "Ctrl+Alt+C" };
        recorder.BeginRecording();

        recorder.Capture(ModifierKeys.None, Key.Escape);

        Assert.Equal("Ctrl+Alt+C", recorder.Shortcut);
        Assert.False(recorder.IsRecording);
    });

    [Fact]
    public void ModifierAlone_KeepsWaiting() => Ui.Run(() =>
    {
        var recorder = new ShortcutRecorder { Shortcut = "Ctrl+Alt+C" };
        recorder.BeginRecording();

        recorder.Capture(ModifierKeys.Control, Key.LeftCtrl);

        Assert.True(recorder.IsRecording);
        Assert.Null(recorder.Message);
    });

    [Fact]
    public void PlainLetter_AsksForAModifier() => Ui.Run(() =>
    {
        var recorder = new ShortcutRecorder { Shortcut = "Ctrl+Alt+C" };
        recorder.BeginRecording();

        recorder.Capture(ModifierKeys.None, Key.K);

        Assert.Equal("Ctrl+Alt+C", recorder.Shortcut);
        Assert.True(recorder.IsRecording);
        Assert.NotNull(recorder.Message);
    });

    [Fact]
    public void Conflict_ShowsInlineAndKeepsRecording() => Ui.Run(() =>
    {
        var recorder = new ShortcutRecorder
        {
            Shortcut = "Ctrl+Alt+C",
            FindConflict = keys => keys == "Ctrl+Alt+H" ? "Already used by Show or hide captions." : null,
        };
        recorder.BeginRecording();

        recorder.Capture(CtrlAlt, Key.H);

        Assert.Equal("Already used by Show or hide captions.", recorder.Message);
        Assert.Equal("Ctrl+Alt+C", recorder.Shortcut);
        Assert.True(recorder.IsRecording);

        recorder.Capture(CtrlAlt, Key.J);

        Assert.Equal("Ctrl+Alt+J", recorder.Shortcut);
        Assert.Null(recorder.Message);
    });
}
