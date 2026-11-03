namespace Captly.Core.Settings;

public enum ThemeChoice { System, Light, Dark }
public enum SourceMode { Auto, Pinned, System }
public enum CaptionStyle { Classic, Glass, Outline, HighContrast }
public enum PositionMode { PinToWindow, Fixed }
public enum FullscreenPosition { ScreenBottom, SameAsWindow }
public enum EngineDevice { Auto, Gpu, Cpu }

public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public bool SetupCompleted { get; init; }
    public GeneralSettings General { get; init; } = new();
    public SourceSettings Source { get; init; } = new();
    public EngineSettings Engine { get; init; } = new();
    public AppearanceSettings Appearance { get; init; } = new();
    public PositionSettings Position { get; init; } = new();
    public TranscriptSettings Transcripts { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
}

public sealed record GeneralSettings
{
    public bool StartWithWindows { get; init; }
    public bool AutoCaption { get; init; } = true;
    public bool PauseWithVideo { get; init; } = true;
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;
    public string Language { get; init; } = "en";
    public bool CheckUpdates { get; init; } = true;
}

public sealed record SourceSettings
{
    public SourceMode Mode { get; init; } = SourceMode.Auto;
    public string? PinnedExe { get; init; }
    public int SwitchDelayMs { get; init; } = 2000;
}

public sealed record EngineSettings
{
    public string Model { get; init; } = "moonshine-tiny-streaming";

    /// <summary>Only a model that can use a graphics card honours Gpu. Today's run on the processor.</summary>
    public EngineDevice Device { get; init; } = EngineDevice.Auto;
    public string? AdapterId { get; init; }
    public string Language { get; init; } = "en";
    public bool Vad { get; init; } = true;
}

public sealed record AppearanceSettings
{
    public CaptionStyle Style { get; init; } = CaptionStyle.Glass;
    public string FontFamily { get; init; } = "Inter";
    public int FontSizeMin { get; init; } = 18;
    public int FontSizeMax { get; init; } = 40;
    public int TextSize { get; init; } = 22;
    public string TextColor { get; init; } = "#FFFFFF";
    public double BackgroundOpacity { get; init; } = 0.66;
    public int MaxLines { get; init; } = 2;
    public int CharsPerLine { get; init; } = 42;
    public int ClearAfterMs { get; init; } = 4000;
}

public sealed record FixedPosition(double X, double Y);

public sealed record PositionSettings
{
    public PositionMode Mode { get; init; } = PositionMode.PinToWindow;
    public int BottomOffsetPercent { get; init; } = 12;
    public int MaxWidthPercent { get; init; } = 80;
    public FullscreenPosition Fullscreen { get; init; } = FullscreenPosition.ScreenBottom;
    public bool ClickThrough { get; init; } = true;
    public FixedPosition? Fixed { get; init; }
}

public sealed record TranscriptSettings
{
    public bool Enabled { get; init; } = true;
    public string Folder { get; init; } = @"%USERPROFILE%\Documents\Captly";
    public bool Txt { get; init; } = true;
    public bool Srt { get; init; } = true;
    public bool NameAfterVideo { get; init; } = true;
}

public sealed record HotkeySettings
{
    public string Toggle { get; init; } = "Ctrl+Alt+C";
    public string Hide { get; init; } = "Ctrl+Alt+H";
    public string NextSource { get; init; } = "Ctrl+Alt+S";
    public string OpenTranscript { get; init; } = "Ctrl+Alt+T";
    public string OpenSettings { get; init; } = "Ctrl+Alt+O";
}
