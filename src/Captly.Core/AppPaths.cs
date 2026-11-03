namespace Captly.Core;

public sealed record AppPaths(string RoamingRoot, string LocalRoot)
{
    /// <summary>Points settings, logs and models at another folder, as a fresh install would have. For testing.</summary>
    public const string OverrideVariable = "CAPTLY_DATA";

    public string SettingsFile => Path.Combine(RoamingRoot, "settings.json");
    public string LogFolder => Path.Combine(LocalRoot, "logs");
    public string ModelFolder => Path.Combine(LocalRoot, "models");
    public string DebugWavFile => Path.Combine(LocalRoot, "debug.wav");

    public static AppPaths ForCurrentUser(Func<string, string?>? environment = null)
    {
        var root = (environment ?? Environment.GetEnvironmentVariable)(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(root)) return new AppPaths(Path.Combine(root, "Roaming"), Path.Combine(root, "Local"));

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Captly"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Captly"));
    }
}
