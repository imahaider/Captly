using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Captly.Core.Shell;
using Captly.UI.Controls;

namespace Captly.UI.Settings;

/// <summary>The Settings window's content: the shell, its eight pages, and their models.</summary>
public partial class SettingsView : UserControl
{
    private readonly SettingsModel _model;

    public SettingsView(SettingsModel model)
    {
        InitializeComponent();
        _model = model;

        Add("General", "General", "#8E8E93", model.General);
        Add("Source", "Source", "#0A84FF", model.Source);
        Add("Appearance", "Appearance", "#AF52DE", model.Appearance);
        Add("Position", "Position", "#FF9500", model.Position);
        Add("Speech Model", "SpeechModel", "#34C759", model.Speech);
        Add("Transcripts", "Transcripts", "#30B0C7", model.Transcripts);
        Add("Shortcuts", "Shortcuts", "#636366", model.Shortcuts, string.Join(' ', Hotkeys.All.Select(h => h.Label)));
        Add("About", "About", "#8E8E93", model.About, pinned: true, sidebarLabel: "About Captly", iconKey: "About");

        Shell.SelectedPageChanged += OnPageChanged;
        Shell.SelectedPage = Shell.Pages[0];

        AddHandler(ModelRow.DownloadRequestedEvent, new RoutedEventHandler((_, e) => _ = model.Speech.DownloadAsync(RowModel(e))));
        AddHandler(ModelRow.UseRequestedEvent, new RoutedEventHandler((_, e) => model.Speech.Use(RowModel(e))));
    }

    public SettingsShell SettingsShell => Shell;

    /// <summary>Shows a page by its title, such as "Source".</summary>
    public void ShowPage(string title)
    {
        var page = Shell.Pages.FirstOrDefault(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
        if (page != null) Shell.SelectedPage = page;
    }

    /// <summary>The meters only run fast while the Source screen is open.</summary>
    public void StopLiveUpdates() => _model.Source.IsShowing = false;

    private void Add(string title, string key, string tile, object model, string? terms = null, bool pinned = false, string? sidebarLabel = null, string? iconKey = null)
    {
        var content = (FrameworkElement)FindResource(key);
        content.DataContext = model;
        Shell.Pages.Add(new SettingsPage
        {
            Title = title,
            SidebarLabel = sidebarLabel,
            Icon = (Geometry)FindResource($"Icon.{iconKey ?? key}"),
            TileBrush = (Brush)new BrushConverter().ConvertFromString(tile)!,
            Content = content,
            IsPinned = pinned,
            SearchTerms = terms,
        });
    }

    private void OnPageChanged(SettingsPage? page)
    {
        _model.Source.IsShowing = page?.Title == "Source" && IsVisible;
        if (page?.Title == "Transcripts") _model.Transcripts.Refresh();
    }

    private static ModelRowModel RowModel(RoutedEventArgs e) => (ModelRowModel)((FrameworkElement)e.OriginalSource).DataContext;

    // The recorder asks the row whether a new shortcut clashes with another Captly shortcut.
    private void Recorder_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ShortcutRecorder { DataContext: ShortcutRowModel row } recorder) recorder.FindConflict = row.FindConflict;
    }
}
