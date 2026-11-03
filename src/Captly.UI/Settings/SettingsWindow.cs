using System.Windows;
using Captly.Core.Shell;
using Captly.UI.Controls;

namespace Captly.UI.Settings;

/// <summary>The Settings window (PLAN.md §6.3): 920 × 640, resizable down to 760 × 560.</summary>
public sealed class SettingsWindow : CaptlyWindow
{
    // The page header is the drag band, so the window moves from its top edge as other apps do.
    private const double HeaderHeight = 53;

    public SettingsWindow(SettingsModel model)
    {
        Title = "Captly Settings";
        Width = 920;
        Height = 640;
        MinWidth = 760;
        MinHeight = 560;
        CaptionHeight = HeaderHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        View = new SettingsView(model);
        Content = View;
        IsVisibleChanged += (_, _) => model.Source.IsShowing = IsVisible && View.SettingsShell.SelectedPage?.Title == "Source";
        Closed += (_, _) => View.StopLiveUpdates();
    }

    public SettingsView View { get; }
}
