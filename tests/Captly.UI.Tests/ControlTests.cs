using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Captly.Core.Settings;
using Captly.UI.Controls;
using Captly.UI.Gallery;

namespace Captly.UI.Tests;

public class ControlTests
{
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    // Lays a tree out as a window would, without one. WPF only applies a default template to a
    // control that has a parent, so the element needs a host.
    private static T Laid<T>(T element, double width = 640, double height = double.PositiveInfinity) where T : FrameworkElement
    {
        var host = new Border { Child = element };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        Ui.Settle();
        return element;
    }

    [Theory]
    [InlineData(EffectiveTheme.Light)]
    [InlineData(EffectiveTheme.Dark)]
    [InlineData(EffectiveTheme.HighContrast)]
    public void Gallery_BuildsInEveryTheme(EffectiveTheme theme) => Ui.Run(() =>
    {
        var gallery = new GalleryContent();
        gallery.Resources.MergedDictionaries.Add(ThemeManager.LoadTokens(theme));

        Laid(gallery);

        Assert.True(gallery.ActualHeight > 500);
    });

    [Fact]
    public void TwoGalleryPanes_KeepTheirOwnSelections() => Ui.Run(() =>
    {
        var panes = new StackPanel();
        panes.Children.Add(new GalleryContent());
        panes.Children.Add(new GalleryContent());

        Laid(panes);

        var checkedPerPane = panes.Children.Cast<DependencyObject>()
            .Select(pane => Walk(pane).OfType<RadioButton>().Count(r => r.IsChecked == true));
        Assert.All(checkedPerPane, count => Assert.Equal(3, count));
    });

    [Fact]
    public void DisabledSegment_CannotBeReachedWithArrowKeys() => Ui.Run(() =>
    {
        var control = new SegmentedControl { SelectedIndex = 0 };
        control.Items.Add(new SegmentedItem { Content = "Automatic" });
        control.Items.Add(new SegmentedItem { Content = "Graphics card", IsEnabled = false });
        control.Items.Add(new SegmentedItem { Content = "Processor" });
        Laid(control);

        control.Move(1);

        Assert.Equal(2, control.SelectedIndex);
    });

    [Fact]
    public void EveryInteractiveControl_HasAScreenReaderName() => Ui.Run(() =>
    {
        var gallery = Laid(new GalleryContent());

        var unnamed = Walk(gallery).OfType<UIElement>()
            .Where(e => e is ButtonBase or ComboBox or Slider or ListBox && e is not SegmentedControl)
            .Where(e => e is not (RepeatButton or Thumb) && Parent(e) is not MacSelect)
            .Select(e => (Element: e, Peer: UIElementAutomationPeer.CreatePeerForElement(e)))
            .Where(p => string.IsNullOrWhiteSpace(p.Peer?.GetName()))
            .Select(p => p.Element.GetType().Name)
            .ToList();

        Assert.Empty(unnamed);
    });

    private static DependencyObject? Parent(DependencyObject element) => (element as FrameworkElement)?.TemplatedParent;

    [Fact]
    public void ToggleSwitch_ExposesTheTogglePattern() => Ui.Run(() =>
    {
        var toggle = new ToggleSwitch();
        var peer = UIElementAutomationPeer.CreatePeerForElement(toggle);

        Assert.NotNull(peer.GetPattern(PatternInterface.Toggle));
    });

    [Theory]
    [InlineData(40, 24, 20)]
    [InlineData(46, 28, 24)]
    public void ToggleSwitch_KnobFitsItsTrack(double width, double height, double knobSize) => Ui.Run(() =>
    {
        var toggle = Laid(new ToggleSwitch { Width = width, Height = height });
        var knob = Walk(toggle).OfType<System.Windows.Shapes.Ellipse>().Single();

        Assert.Equal(knobSize, knob.ActualWidth);
    });

    [Fact]
    public void SegmentedControl_ArrowKeysMoveAndWrap() => Ui.Run(() =>
    {
        var control = new SegmentedControl { SelectedIndex = 0 };
        foreach (var name in new[] { "Light", "Dark", "Match Windows" }) control.Items.Add(new SegmentedItem { Content = name });

        control.Move(1);
        Assert.Equal(1, control.SelectedIndex);

        control.Move(1);
        control.Move(1);
        Assert.Equal(0, control.SelectedIndex);

        control.Move(-1);
        Assert.Equal(2, control.SelectedIndex);
    });

    [Fact]
    public void SettingRow_NamesItsControlAfterTheLabel() => Ui.Run(() =>
    {
        var toggle = new ToggleSwitch();

        _ = new SettingRow { Label = "Save transcripts", Content = toggle };

        Assert.Equal("Save transcripts", AutomationProperties.GetName(toggle));
    });

    [Fact]
    public void SettingRow_KeepsAnExistingName() => Ui.Run(() =>
    {
        var toggle = new ToggleSwitch();
        AutomationProperties.SetName(toggle, "Mine");

        _ = new SettingRow { Label = "Save transcripts", Content = toggle };

        Assert.Equal("Mine", AutomationProperties.GetName(toggle));
    });

    [Fact]
    public void GroupCard_DrawsHairlinesOnlyBetweenRows() => Ui.Run(() =>
    {
        var card = new GroupCard();
        for (var i = 0; i < 3; i++) card.Items.Add(new SettingRow { Label = $"Row {i}" });

        var items = Walk(Laid(card)).OfType<GroupCardItem>().ToList();

        Assert.Equal([false, true, true], items.Select(i => i.ShowSeparator));
    });

    [Fact]
    public void SettingsShell_BackAndForwardFollowHistory() => Ui.Run(() =>
    {
        var shell = new SettingsShell();
        SettingsPage a = new() { Title = "A" }, b = new() { Title = "B" }, c = new() { Title = "C" };
        shell.SelectedPage = a;
        shell.SelectedPage = b;
        shell.SelectedPage = c;

        shell.GoBack();
        Assert.Same(b, shell.SelectedPage);
        shell.GoBack();
        Assert.Same(a, shell.SelectedPage);
        shell.GoBack();
        Assert.Same(a, shell.SelectedPage);

        shell.GoForward();
        Assert.Same(b, shell.SelectedPage);

        shell.SelectedPage = c;
        shell.GoForward();
        Assert.Same(c, shell.SelectedPage);
    });

    [Fact]
    public void SettingsPage_UsesItsSidebarLabelWhenGiven()
    {
        Assert.Equal("About Captly", new SettingsPage { Title = "About", SidebarLabel = "About Captly" }.NavTitle);
        Assert.Equal("General", new SettingsPage { Title = "General" }.NavTitle);
    }

    [Theory]
    [InlineData(CaptionStyle.Glass)]
    [InlineData(CaptionStyle.Classic)]
    [InlineData(CaptionStyle.Outline)]
    [InlineData(CaptionStyle.HighContrast)]
    public void CaptionBubble_KeepsItsTextInsideTheBox(CaptionStyle style) => Ui.Run(() =>
    {
        var bubble = Laid(new CaptionBubble { CaptionStyle = style, Text = "The fan pulls in air, and most of it" });
        var text = Walk(bubble).OfType<OutlinedText>().Single();

        var offset = text.TranslatePoint(default, bubble);

        Assert.True(offset.X >= 0);
        Assert.True(offset.X + text.ActualWidth <= bubble.ActualWidth + 1);
    });

    [Fact]
    public void CaptionBubble_WrapsToTheWidthItIsGiven() => Ui.Run(() =>
    {
        var bubble = Laid(new CaptionBubble { Text = "A long caption that cannot possibly fit on one line of this very narrow width" }, width: 240);

        Assert.True(bubble.ActualWidth <= 240);
        Assert.True(bubble.ActualHeight > 60);
    });

    [Fact]
    public void AppTile_ScalesWithItsSize() => Ui.Run(() =>
    {
        var tile = Laid(new AppTile { Size = 76 });

        Assert.Equal(76, tile.ActualWidth);
        Assert.Equal(76, tile.ActualHeight);
    });

    [Fact]
    public void MacSlider_FormatsItsValue() => Ui.Run(() =>
    {
        var slider = new MacSlider { Minimum = 0, Maximum = 100, ValueFormat = "{0:0}%" };

        slider.Value = 66;

        Assert.Equal("66%", slider.ValueText);
    });
}
