using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Captly.Core.Settings;

public sealed class SettingsStore(
    string path,
    ILogger<SettingsStore> logger,
    IReadOnlyList<ISettingsMigration>? migrations = null,
    int currentVersion = AppSettings.CurrentVersion)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly IReadOnlyList<ISettingsMigration> _migrations = migrations ?? SettingsMigrator.All;

    /// <summary>
    /// Reads the settings. A missing file gives defaults. A file that cannot be read is moved
    /// aside rather than lost, so one bad hand edit never stops the app from starting.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new JsonException("Settings file is empty.");
            var fileVersion = root["version"]?.GetValue<int>() ?? 1;

            // A newer file, from a later Captly, is kept aside before this version rewrites it.
            if (fileVersion > currentVersion) Backup($".v{fileVersion}.bak");

            var migrated = SettingsMigrator.Migrate(root, _migrations, currentVersion);
            if (migrated) Backup($".v{fileVersion}.bak");

            var settings = root.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
            settings = settings with { Version = currentVersion };
            if (migrated)
            {
                Save(settings);
                logger.LogInformation("Settings migrated from version {From} to {To}", fileVersion, currentVersion);
            }
            return settings;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            var aside = path + $".bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, aside, overwrite: true);
            logger.LogWarning(e, "Settings could not be read. Moved to {Aside} and using defaults", aside);
            return new AppSettings();
        }
    }

    /// <summary>Writes through a temporary file, so a crash mid-save cannot leave half a file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private void Backup(string suffix) => File.Copy(path, path + suffix, overwrite: true);
}
