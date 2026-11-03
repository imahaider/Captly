using System.Text;
using Captly.Core.Settings;

namespace Captly.Core.Transcripts;

/// <summary>File names and the folder for transcripts. Pure apart from checking which names are taken.</summary>
public static class TranscriptNames
{
    private const int MaxNameLength = 100;
    private const string InvalidCharacters = "<>:\"/\\|?*";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static readonly string[] Extensions = [".txt", ".srt"];

    /// <summary>"2026-11-03 - Inside a jet engine", or "2026-11-03 14-05 - Google Chrome" with no title.</summary>
    public static string Stem(string? title, string app, DateTimeOffset at, bool nameAfterVideo) =>
        nameAfterVideo && !string.IsNullOrWhiteSpace(title)
            ? $"{at:yyyy-MM-dd} - {Clean(title)}"
            : $"{at:yyyy-MM-dd HH-mm} - {Clean(app)}";

    /// <summary>Makes text safe as a Windows file name, on any platform.</summary>
    public static string Clean(string text)
    {
        var safe = new StringBuilder(text.Length);
        foreach (var c in text) safe.Append(char.IsControl(c) || InvalidCharacters.Contains(c) ? ' ' : c);

        var name = string.Join(' ', safe.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        name = name.TrimEnd('.', ' ');
        if (name.Length == 0) return "Untitled";
        return ReservedNames.Contains(name) ? "_" + name : name;
    }

    /// <summary>The stem, or the stem with " (2)", " (3)" and so on, whichever no transcript uses yet.</summary>
    public static string Unique(string folder, string stem)
    {
        bool Taken(string candidate) => Extensions.Any(e => File.Exists(Path.Combine(folder, candidate + e)));
        if (!Taken(stem)) return stem;
        for (var n = 2; ; n++)
            if (!Taken($"{stem} ({n})")) return $"{stem} ({n})";
    }

    /// <summary>
    /// The default setting means the user's real Documents folder, which may have moved, for
    /// example into OneDrive. Any other setting may use environment variables.
    /// </summary>
    public static string ResolveFolder(string setting) =>
        string.Equals(setting, new TranscriptSettings().Folder, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Captly")
            : Environment.ExpandEnvironmentVariables(setting);
}
