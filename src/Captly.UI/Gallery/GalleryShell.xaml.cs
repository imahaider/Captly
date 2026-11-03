using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Captly.UI.Controls;

namespace Captly.UI.Gallery;

public partial class GalleryShell : UserControl
{
    public GalleryShell() : this("General")
    {
    }

    public GalleryShell(string page)
    {
        InitializeComponent();
        Add("General", "General", "#8E8E93");
        Add("Source", "Source", "#0A84FF");
        Add("Appearance", "Appearance", "#AF52DE");
        Add("Position", "Position", "#FF9500");
        Add("Speech Model", "SpeechModel", "#34C759");
        Add("Transcripts", "Transcripts", "#30B0C7");
        Add("Shortcuts", "Shortcuts", "#636366");
        Shell.Pages.Add(new SettingsPage
        {
            Title = "About",
            SidebarLabel = "About Captly",
            Icon = (Geometry)FindResource("Icon.About"),
            TileBrush = Brush("#8E8E93"),
            IsPinned = true,
        });
        Shell.SelectedPage = Shell.Pages.First(p => p.Title == page);
    }

    public static IReadOnlyList<string> PageNames { get; } = ["General", "Source", "Appearance", "Speech Model", "Transcripts", "Shortcuts"];

    private void Add(string title, string key, string tile) => Shell.Pages.Add(new SettingsPage
    {
        Title = title,
        Icon = (Geometry)FindResource($"Icon.{key}"),
        TileBrush = Brush(tile),
        Content = TryFindResource(key),
    });

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
}
