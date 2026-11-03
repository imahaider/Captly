using Captly.Core.Settings;

namespace Captly.Core.Shell;

/// <summary>
/// A Settings screen's view model. Properties read the settings in force and write changes
/// through <see cref="SettingsService"/>, so they apply at once and persist.
/// </summary>
public abstract class SettingsPageModel : Observable
{
    protected SettingsPageModel(SettingsService settings)
    {
        Settings = settings;
        settings.Changed += (_, _) => RaiseAll();
    }

    protected SettingsService Settings { get; }

    protected AppSettings Current => Settings.Current;

    protected void Change(Func<AppSettings, AppSettings> change) => Settings.Update(change);
}

public sealed class GeneralPageModel(SettingsService settings, UpdateStatus updates) : SettingsPageModel(settings)
{
    public bool StartWithWindows
    {
        get => Current.General.StartWithWindows;
        set => Change(s => s with { General = s.General with { StartWithWindows = value } });
    }

    public bool AutoCaption
    {
        get => Current.General.AutoCaption;
        set => Change(s => s with { General = s.General with { AutoCaption = value } });
    }

    public bool PauseWithVideo
    {
        get => Current.General.PauseWithVideo;
        set => Change(s => s with { General = s.General with { PauseWithVideo = value } });
    }

    /// <summary>The segments read Light, Dark, Match Windows.</summary>
    public int ThemeIndex
    {
        get => Current.General.Theme switch { ThemeChoice.Light => 0, ThemeChoice.Dark => 1, _ => 2 };
        set => Change(s => s with { General = s.General with { Theme = value switch { 0 => ThemeChoice.Light, 1 => ThemeChoice.Dark, _ => ThemeChoice.System } } });
    }

    public IReadOnlyList<Choice<string>> Languages { get; } = [new("English", "en")];

    public Choice<string> Language
    {
        get => Languages.FirstOrDefault(l => l.Value == Current.General.Language) ?? Languages[0];
        set => Change(s => s with { General = s.General with { Language = value.Value } });
    }

    public bool CheckUpdates
    {
        get => Current.General.CheckUpdates;
        set => Change(s => s with { General = s.General with { CheckUpdates = value } });
    }

    public UpdateStatus Updates => updates;

    public string VersionLabel => $"Captly {updates.Version}";
}

public sealed class AppearancePageModel(SettingsService settings) : SettingsPageModel(settings)
{
    public const string White = "#FFFFFF";
    public const string Yellow = "#FFD60A";
    public const string Cyan = "#5AC8FA";
    public const string Green = "#30D158";

    private AppearanceSettings A => Current.Appearance;

    public bool IsClassic { get => A.Style == CaptionStyle.Classic; set => SetStyle(value, CaptionStyle.Classic); }
    public bool IsGlass { get => A.Style == CaptionStyle.Glass; set => SetStyle(value, CaptionStyle.Glass); }
    public bool IsOutline { get => A.Style == CaptionStyle.Outline; set => SetStyle(value, CaptionStyle.Outline); }
    public bool IsHighContrast { get => A.Style == CaptionStyle.HighContrast; set => SetStyle(value, CaptionStyle.HighContrast); }

    public CaptionStyle Style => A.Style;

    public double TextSize
    {
        get => A.TextSize;
        set => Change(s => s with { Appearance = s.Appearance with { TextSize = (int)Math.Round(value) } });
    }

    /// <summary>The bubble's opacity as a percentage, for the slider.</summary>
    public double BackgroundPercent
    {
        get => Math.Round(A.BackgroundOpacity * 100);
        set => Change(s => s with { Appearance = s.Appearance with { BackgroundOpacity = Math.Round(value) / 100 } });
    }

    public double BackgroundOpacity => A.BackgroundOpacity;

    public string TextColor => A.TextColor;

    public bool IsWhite { get => IsColor(White); set => SetColor(value, White); }
    public bool IsYellow { get => IsColor(Yellow); set => SetColor(value, Yellow); }
    public bool IsCyan { get => IsColor(Cyan); set => SetColor(value, Cyan); }
    public bool IsGreen { get => IsColor(Green); set => SetColor(value, Green); }

    public IReadOnlyList<Choice<int>> LineChoices { get; } = [new("1", 1), new("2", 2), new("3", 3)];

    public Choice<int> Lines
    {
        get => LineChoices.FirstOrDefault(c => c.Value == A.MaxLines) ?? LineChoices[1];
        set => Change(s => s with { Appearance = s.Appearance with { MaxLines = value.Value } });
    }

    public IReadOnlyList<Choice<int>> HideAfterChoices { get; } = [new("2 seconds", 2000), new("4 seconds", 4000), new("8 seconds", 8000)];

    public Choice<int> HideAfter
    {
        get => HideAfterChoices.FirstOrDefault(c => c.Value == A.ClearAfterMs) ?? HideAfterChoices[1];
        set => Change(s => s with { Appearance = s.Appearance with { ClearAfterMs = value.Value } });
    }

    private bool IsColor(string color) => string.Equals(A.TextColor, color, StringComparison.OrdinalIgnoreCase);

    private void SetStyle(bool chosen, CaptionStyle style)
    {
        if (chosen) Change(s => s with { Appearance = s.Appearance with { Style = style } });
    }

    private void SetColor(bool chosen, string color)
    {
        if (chosen) Change(s => s with { Appearance = s.Appearance with { TextColor = color } });
    }
}

public sealed class PositionPageModel(SettingsService settings) : SettingsPageModel(settings)
{
    private PositionSettings P => Current.Position;

    public bool IsPinToWindow { get => P.Mode == PositionMode.PinToWindow; set => SetMode(value, PositionMode.PinToWindow); }

    public bool IsFixed { get => P.Mode == PositionMode.Fixed; set => SetMode(value, PositionMode.Fixed); }

    public double BottomOffset
    {
        get => P.BottomOffsetPercent;
        set => Change(s => s with { Position = s.Position with { BottomOffsetPercent = (int)Math.Round(value) } });
    }

    public double MaxWidth
    {
        get => P.MaxWidthPercent;
        set => Change(s => s with { Position = s.Position with { MaxWidthPercent = (int)Math.Round(value) } });
    }

    public IReadOnlyList<Choice<FullscreenPosition>> FullscreenChoices { get; } =
        [new("Bottom of the screen", FullscreenPosition.ScreenBottom), new("Same as in the window", FullscreenPosition.SameAsWindow)];

    public Choice<FullscreenPosition> Fullscreen
    {
        get => FullscreenChoices.First(c => c.Value == P.Fullscreen);
        set => Change(s => s with { Position = s.Position with { Fullscreen = value.Value } });
    }

    public bool ClickThrough
    {
        get => P.ClickThrough;
        set => Change(s => s with { Position = s.Position with { ClickThrough = value } });
    }

    private void SetMode(bool chosen, PositionMode mode)
    {
        if (chosen) Change(s => s with { Position = s.Position with { Mode = mode } });
    }
}

/// <summary>One shortcut on the Shortcuts screen.</summary>
public sealed class ShortcutRowModel(SettingsService settings, HotkeyAction action) : Observable
{
    private string? _problem;

    public HotkeyAction Action => action;

    public string Label => Hotkeys.Label(action);

    public string Shortcut
    {
        get => Hotkeys.Get(settings.Current.Hotkeys, action);
        set => settings.Update(s => s with { Hotkeys = Hotkeys.With(s.Hotkeys, action, value) });
    }

    /// <summary>Set when Windows refused the shortcut, because another app holds it.</summary>
    public string? Problem { get => _problem; set => Set(ref _problem, value); }

    /// <summary>For the recorder: the message for a clash with another Captly shortcut.</summary>
    public string? FindConflict(string candidate) => Hotkeys.Conflict(settings.Current.Hotkeys, action, candidate);

    internal void Refresh() => Raise(nameof(Shortcut));
}

public sealed class ShortcutsPageModel : SettingsPageModel
{
    public ShortcutsPageModel(SettingsService settings) : base(settings)
    {
        Rows = Hotkeys.All.Select(a => new ShortcutRowModel(settings, a.Action)).ToList();
        settings.Changed += (_, _) => Rows.ForEach(r => r.Refresh());
        RestoreDefaultsCommand = new Command(() => Change(s => s with { Hotkeys = new HotkeySettings() }));
    }

    public List<ShortcutRowModel> Rows { get; }

    public Command RestoreDefaultsCommand { get; }

    /// <summary>Shows which shortcuts Windows refused. Others are cleared.</summary>
    public void ShowProblems(IReadOnlyCollection<HotkeyAction> refused)
    {
        foreach (var row in Rows) row.Problem = refused.Contains(row.Action) ? "Another app is using this shortcut." : null;
    }
}

/// <summary>A library Captly is built with, for the About screen.</summary>
public sealed record Credit(string Name, string Role, string Licence)
{
    public string Detail => $"{Role} · {Licence}";
}

public sealed class AboutPageModel(UpdateStatus updates, Action<string> open) : Observable
{
    public const string RepoUrl = "https://github.com/imahaider/Captly";

    public UpdateStatus Updates => updates;

    public string VersionLine => $"Version {updates.Version} · Live captions that stick to your video";

    public string Footer => $"© {DateTime.Now.Year} MAH · Open source under the MIT licence";

    public IReadOnlyList<Credit> Credits { get; } =
    [
        new("Moonshine", "Speech recognition", "MIT"),
        new("ONNX Runtime", "Runs the model", "MIT"),
        new("NAudio", "Audio capture", "MIT"),
        new("H.NotifyIcon", "Tray icon", "MIT"),
        new("Inter", "Typeface", "SIL Open Font License"),
        new("Lucide", "Icons", "ISC"),
    ];

    public Command ViewOnGitHubCommand { get; } = new(() => open(RepoUrl));

    public Command ReportProblemCommand { get; } = new(() => open(RepoUrl + "/issues/new"));
}
