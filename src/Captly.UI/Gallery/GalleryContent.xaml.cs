using System.Windows;
using System.Windows.Controls;

namespace Captly.UI.Gallery;

public partial class GalleryContent : UserControl
{
    private static int _instances;

    public GalleryContent()
    {
        InitializeComponent();
        ScopeRadioGroups(this, ++_instances);
        Loaded += (_, _) => ShowRecording();
    }

    /// <summary>Puts one recorder in its "press the new keys" state, so that look can be checked.</summary>
    internal void ShowRecording() => Recording.BeginRecording();

    // Radio groups span a whole window, so two panes would otherwise share one selection.
    private static void ScopeRadioGroups(DependencyObject parent, int pane)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is RadioButton radio) radio.GroupName += pane;
            ScopeRadioGroups(child, pane);
        }
    }
}
