using System.Text.Json.Nodes;

namespace Captly.Core.Settings;

/// <summary>Upgrades a settings document from <see cref="FromVersion"/> to the next version.</summary>
public interface ISettingsMigration
{
    int FromVersion { get; }
    void Apply(JsonObject settings);
}

public static class SettingsMigrator
{
    /// <summary>Add a migration here whenever <see cref="AppSettings.CurrentVersion"/> goes up.</summary>
    public static readonly IReadOnlyList<ISettingsMigration> All = [];

    /// <summary>Returns true if the document changed. A file with no version counts as version 1.</summary>
    public static bool Migrate(JsonObject settings, IReadOnlyList<ISettingsMigration> migrations, int targetVersion)
    {
        var version = settings["version"]?.GetValue<int>() ?? 1;
        var changed = false;
        while (version < targetVersion)
        {
            var migration = migrations.FirstOrDefault(m => m.FromVersion == version)
                ?? throw new InvalidOperationException($"No settings migration from version {version}.");
            migration.Apply(settings);
            version++;
            settings["version"] = version;
            changed = true;
        }
        return changed;
    }
}
