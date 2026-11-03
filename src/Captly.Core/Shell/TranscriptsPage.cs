using System.Collections.ObjectModel;
using Captly.Core.Transcripts;

namespace Captly.Core.Shell;

/// <summary>One line of the Recent list.</summary>
public sealed record RecentTranscript(string Title, string Detail, string Path, Command OpenCommand);

/// <summary>The Transcripts screen (PLAN.md §6.3 #6).</summary>
public sealed class TranscriptsPageModel : SettingsPageModel
{
    public const int RecentCount = 5;

    private readonly IShellServices _shell;
    private readonly Func<DateTime> _today;

    public TranscriptsPageModel(SettingsService settings, IShellServices shell, Func<DateTime>? today = null) : base(settings)
    {
        _shell = shell;
        _today = today ?? (() => DateTime.Today);
        ChangeFolderCommand = new Command(ChangeFolder);
        OpenFolderCommand = new Command(() =>
        {
            Directory.CreateDirectory(Folder);
            _shell.Open(Folder);
        });
    }

    public bool Enabled
    {
        get => Current.Transcripts.Enabled;
        set => Change(s => s with { Transcripts = s.Transcripts with { Enabled = value } });
    }

    public bool Txt
    {
        get => Current.Transcripts.Txt;
        set => Change(s => s with { Transcripts = s.Transcripts with { Txt = value } });
    }

    public bool Srt
    {
        get => Current.Transcripts.Srt;
        set => Change(s => s with { Transcripts = s.Transcripts with { Srt = value } });
    }

    public bool NameAfterVideo
    {
        get => Current.Transcripts.NameAfterVideo;
        set => Change(s => s with { Transcripts = s.Transcripts with { NameAfterVideo = value } });
    }

    /// <summary>The real folder the setting points to.</summary>
    public string Folder => TranscriptNames.ResolveFolder(Current.Transcripts.Folder);

    /// <summary>"Documents › Captly" for folders under Documents, otherwise the full path.</summary>
    public string FolderDisplay => DisplayFolder(Folder, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    public Command ChangeFolderCommand { get; }

    public Command OpenFolderCommand { get; }

    public ObservableCollection<RecentTranscript> Recent { get; } = [];

    public bool HasRecent => Recent.Count > 0;

    /// <summary>Reads the folder again. The view calls it when the screen opens.</summary>
    public void Refresh()
    {
        Recent.Clear();
        foreach (var entry in TranscriptIndex.Recent(Folder, RecentCount))
        {
            var path = entry.TxtPath ?? entry.SrtPath;
            if (path == null) continue;
            Recent.Add(new RecentTranscript(entry.Title, Detail(entry, _today()), path, new Command(() => _shell.Open(path))));
        }
        Raise(nameof(HasRecent));
    }

    /// <summary>Such as "Today · Google Chrome · TXT + SRT".</summary>
    public static string Detail(TranscriptEntry entry, DateTime today)
    {
        var day = entry.LastWritten.Date;
        var when = day == today.Date ? "Today" : day == today.Date.AddDays(-1) ? "Yesterday" : day.ToString("d MMM yyyy");
        var types = string.Join(" + ", new[] { entry.TxtPath != null ? "TXT" : null, entry.SrtPath != null ? "SRT" : null }.OfType<string>());
        return string.Join(" · ", new[] { when, entry.Source, types }.Where(p => !string.IsNullOrEmpty(p)));
    }

    public static string DisplayFolder(string folder, string documents)
    {
        var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var docs = Path.GetFullPath(documents).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(docs, StringComparison.OrdinalIgnoreCase)) return full;
        var rest = full[docs.Length..].Trim(Path.DirectorySeparatorChar);
        return rest.Length == 0 ? "Documents" : "Documents › " + rest.Replace(Path.DirectorySeparatorChar.ToString(), " › ");
    }

    private void ChangeFolder()
    {
        var picked = _shell.PickFolder(Folder);
        if (picked != null) Change(s => s with { Transcripts = s.Transcripts with { Folder = picked } });
    }
}
