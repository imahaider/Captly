using System.Text.Json;
using System.Text.Json.Nodes;
using Captly.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Captly.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("captly-settings-").FullName;
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private SettingsStore Store(IReadOnlyList<ISettingsMigration>? migrations = null, int version = AppSettings.CurrentVersion) =>
        new(SettingsPath, NullLogger<SettingsStore>.Instance, migrations, version);

    // PLAN.md §9
    private const string PlanDefaults = """
        {
          "version": 1,
          "setupCompleted": false,
          "general": { "startWithWindows": false, "autoCaption": true, "pauseWithVideo": true,
                       "theme": "system", "language": "en", "checkUpdates": true },
          "source": { "mode": "auto", "pinnedExe": null, "switchDelayMs": 2000 },
          "engine": { "model": "moonshine-tiny-streaming", "device": "auto", "adapterId": null, "language": "en", "vad": true },
          "appearance": { "style": "glass", "fontFamily": "Inter", "fontSizeMin": 18, "fontSizeMax": 40, "textSize": 22,
                          "textColor": "#FFFFFF", "backgroundOpacity": 0.66, "maxLines": 2, "charsPerLine": 42,
                          "clearAfterMs": 4000 },
          "position": { "mode": "pinToWindow", "bottomOffsetPercent": 12, "maxWidthPercent": 80,
                        "fullscreen": "screenBottom", "clickThrough": true, "fixed": null },
          "transcripts": { "enabled": true, "folder": "%USERPROFILE%\\Documents\\Captly", "txt": true, "srt": true,
                           "nameAfterVideo": true },
          "hotkeys": { "toggle": "Ctrl+Alt+C", "hide": "Ctrl+Alt+H", "nextSource": "Ctrl+Alt+S",
                       "openTranscript": "Ctrl+Alt+T", "openSettings": "Ctrl+Alt+O" }
        }
        """;

    [Fact]
    public void Defaults_MatchThePlan()
    {
        var actual = JsonNode.Parse(JsonSerializer.Serialize(new AppSettings(), SettingsStore.JsonOptions));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(PlanDefaults), actual), actual!.ToJsonString());
    }

    [Fact]
    public void Load_MissingFile_GivesDefaults() => Assert.Equal(new AppSettings().Source, Store().Load().Source);

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var changed = new AppSettings
        {
            SetupCompleted = true,
            General = new GeneralSettings { Theme = ThemeChoice.Dark },
            Appearance = new AppearanceSettings { Style = CaptionStyle.HighContrast, BackgroundOpacity = 0.25 },
            Position = new PositionSettings { Mode = PositionMode.Fixed, Fixed = new FixedPosition(10, 20) },
        };

        Store().Save(changed);
        var loaded = Store().Load();

        Assert.True(loaded.SetupCompleted);
        Assert.Equal(ThemeChoice.Dark, loaded.General.Theme);
        Assert.Equal(changed.Appearance, loaded.Appearance);
        Assert.Equal(changed.Position, loaded.Position);
    }

    [Fact]
    public void Load_MissingSectionsAndUnknownFields_AreTolerated()
    {
        File.WriteAllText(SettingsPath, """{ "version": 1, "general": { "theme": "light" }, "fromTheFuture": 1 }""");

        var loaded = Store().Load();

        Assert.Equal(ThemeChoice.Light, loaded.General.Theme);
        Assert.True(loaded.General.AutoCaption);
        Assert.Equal(new HotkeySettings(), loaded.Hotkeys);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("""{ "general": { "theme": "purple" } }""")]
    [InlineData("""{ "version": "one" }""")]
    [InlineData("[1, 2]")]
    public void Load_BadFile_GivesDefaultsAndKeepsTheFile(string content)
    {
        File.WriteAllText(SettingsPath, content);

        var loaded = Store().Load();

        Assert.Equal(new GeneralSettings(), loaded.General);
        Assert.False(File.Exists(SettingsPath));
        var aside = Assert.Single(Directory.GetFiles(_folder, "settings.json.bad-*"));
        Assert.Equal(content, File.ReadAllText(aside));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile()
    {
        Store().Save(new AppSettings());
        Assert.Equal(["settings.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public void Migrate_RunsEachStepInOrder()
    {
        var root = JsonNode.Parse("""{ "version": 1, "trace": "" }""")!.AsObject();

        var changed = SettingsMigrator.Migrate(root, [new Step(2, "b"), new Step(1, "a")], 3);

        Assert.True(changed);
        Assert.Equal(3, root["version"]!.GetValue<int>());
        Assert.Equal("ab", root["trace"]!.GetValue<string>());
    }

    [Fact]
    public void Migrate_AtCurrentVersion_ChangesNothing()
    {
        var root = JsonNode.Parse("""{ "version": 2 }""")!.AsObject();
        Assert.False(SettingsMigrator.Migrate(root, [new Step(1, "a")], 2));
    }

    [Fact]
    public void Migrate_NoVersionCountsAsOne()
    {
        var root = new JsonObject { ["trace"] = "" };
        SettingsMigrator.Migrate(root, [new Step(1, "a")], 2);
        Assert.Equal("a", root["trace"]!.GetValue<string>());
    }

    [Fact]
    public void Load_OldFile_MigratesSavesAndBacksUp()
    {
        const string old = """{ "version": 1, "general": { "theme": "dark" } }""";
        File.WriteAllText(SettingsPath, old);

        var loaded = Store([new RenameTheme()], version: 2).Load();

        Assert.Equal(2, loaded.Version);
        Assert.Equal(ThemeChoice.Light, loaded.General.Theme);
        Assert.Equal(old, File.ReadAllText(SettingsPath + ".v1.bak"));
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(SettingsPath))!["version"]!.GetValue<int>());
    }

    [Fact]
    public void Load_MissingMigration_KeepsTheFileAside()
    {
        File.WriteAllText(SettingsPath, """{ "version": 1 }""");

        Store([], version: 2).Load();

        Assert.Single(Directory.GetFiles(_folder, "settings.json.bad-*"));
    }

    [Fact]
    public void Load_FileFromNewerVersion_BacksUpAndStillLoads()
    {
        File.WriteAllText(SettingsPath, """{ "version": 9, "general": { "theme": "dark" } }""");

        var loaded = Store().Load();

        Assert.Equal(ThemeChoice.Dark, loaded.General.Theme);
        Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
        Assert.True(File.Exists(SettingsPath + ".v9.bak"));
    }

    private sealed class Step(int from, string mark) : ISettingsMigration
    {
        public int FromVersion => from;
        public void Apply(JsonObject settings) => settings["trace"] = settings["trace"]!.GetValue<string>() + mark;
    }

    private sealed class RenameTheme : ISettingsMigration
    {
        public int FromVersion => 1;
        public void Apply(JsonObject settings) => settings["general"]!["theme"] = "light";
    }
}
