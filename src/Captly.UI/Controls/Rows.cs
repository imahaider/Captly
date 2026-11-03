using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Captly.Core.Shell;

namespace Captly.UI.Controls;

/// <summary>White rounded card with hairlines between its rows, and an optional heading above.</summary>
[ContentProperty(nameof(Items))]
public sealed class GroupCard : ItemsControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(GroupCard), new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderDetailProperty = DependencyProperty.Register(
        nameof(HeaderDetail), typeof(string), typeof(GroupCard), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(GroupCard), new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderActionProperty = DependencyProperty.Register(
        nameof(HeaderAction), typeof(object), typeof(GroupCard), new PropertyMetadata(null));

    static GroupCard() => DefaultStyleKeyProperty.OverrideMetadata(typeof(GroupCard), new FrameworkPropertyMetadata(typeof(GroupCard)));

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Quiet text beside the header, such as "Updates live".</summary>
    public string? HeaderDetail
    {
        get => (string?)GetValue(HeaderDetailProperty);
        set => SetValue(HeaderDetailProperty, value);
    }

    /// <summary>A line of text between the header and the card.</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Right-aligned on the header line, such as an "Open Folder" link.</summary>
    public object? HeaderAction
    {
        get => GetValue(HeaderActionProperty);
        set => SetValue(HeaderActionProperty, value);
    }

    protected override DependencyObject GetContainerForItemOverride() => new GroupCardItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => false;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        ((GroupCardItem)element).ShowSeparator = ItemContainerGenerator.IndexFromContainer(element) > 0;
    }
}

/// <summary>Holds one row of a card and draws the hairline above it.</summary>
public sealed class GroupCardItem : ContentControl
{
    public static readonly DependencyProperty ShowSeparatorProperty = DependencyProperty.Register(
        nameof(ShowSeparator), typeof(bool), typeof(GroupCardItem), new PropertyMetadata(false));

    static GroupCardItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GroupCardItem), new FrameworkPropertyMetadata(typeof(GroupCardItem)));
        FocusableProperty.OverrideMetadata(typeof(GroupCardItem), new FrameworkPropertyMetadata(false));
    }

    public bool ShowSeparator
    {
        get => (bool)GetValue(ShowSeparatorProperty);
        set => SetValue(ShowSeparatorProperty, value);
    }
}

/// <summary>Label and description on the left, control on the right. The whole row is the hit target.</summary>
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SettingRow), new PropertyMetadata(null, (d, _) => ((SettingRow)d).NameContent()));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(null, (d, e) => d.SetValue(HasDescriptionKey, e.NewValue != null)));

    private static readonly DependencyPropertyKey HasDescriptionKey = DependencyProperty.RegisterReadOnly(
        nameof(HasDescription), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    public static readonly DependencyProperty HasDescriptionProperty = HasDescriptionKey.DependencyProperty;

    public static readonly DependencyProperty IsWideProperty = DependencyProperty.Register(
        nameof(IsWide), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    static SettingRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));
        FocusableProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(false));
    }

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool HasDescription => (bool)GetValue(HasDescriptionProperty);

    /// <summary>For sliders: the control fills the row beside a fixed-width label.</summary>
    public bool IsWide
    {
        get => (bool)GetValue(IsWideProperty);
        set => SetValue(IsWideProperty, value);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        NameContent();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (e.Handled || Content is not UIElement control || control.IsMouseOver || !control.IsEnabled) return;

        if (control is ToggleButton toggle) toggle.IsChecked = toggle.IsChecked != true;
        control.Focus();
        e.Handled = true;
    }

    // Screen readers name the control after the row, unless it already has its own name.
    private void NameContent()
    {
        if (Content is DependencyObject control && Label != null && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
            AutomationProperties.SetName(control, Label);
    }
}

/// <summary>Radio row for a capturing app: icon, name, subtitle, badge and level meter.</summary>
public sealed class AppSourceRow : RadioButton
{
    public static readonly DependencyProperty AppIconProperty = DependencyProperty.Register(
        nameof(AppIcon), typeof(ImageSource), typeof(AppSourceRow), new PropertyMetadata(null));

    public static readonly DependencyProperty AppNameProperty = DependencyProperty.Register(
        nameof(AppName), typeof(string), typeof(AppSourceRow),
        new PropertyMetadata(null, (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(AppSourceRow), new PropertyMetadata(null));

    public static readonly DependencyProperty IsNowPlayingProperty = DependencyProperty.Register(
        nameof(IsNowPlaying), typeof(bool), typeof(AppSourceRow), new PropertyMetadata(false));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(AppSourceRow), new PropertyMetadata(0.0));

    public static readonly DependencyProperty ShowMeterProperty = DependencyProperty.Register(
        nameof(ShowMeter), typeof(bool), typeof(AppSourceRow), new PropertyMetadata(true));

    static AppSourceRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(AppSourceRow), new FrameworkPropertyMetadata(typeof(AppSourceRow)));

    public ImageSource? AppIcon
    {
        get => (ImageSource?)GetValue(AppIconProperty);
        set => SetValue(AppIconProperty, value);
    }

    public string? AppName
    {
        get => (string?)GetValue(AppNameProperty);
        set => SetValue(AppNameProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool IsNowPlaying
    {
        get => (bool)GetValue(IsNowPlayingProperty);
        set => SetValue(IsNowPlayingProperty, value);
    }

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>"All system audio" has no single session to meter.</summary>
    public bool ShowMeter
    {
        get => (bool)GetValue(ShowMeterProperty);
        set => SetValue(ShowMeterProperty, value);
    }
}

/// <summary>One speech model in the list, with its download or use action.</summary>
[TemplatePart(Name = "PART_Download", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Use", Type = typeof(ButtonBase))]
public sealed class ModelRow : Control
{
    public static readonly DependencyProperty ModelNameProperty = DependencyProperty.Register(
        nameof(ModelName), typeof(string), typeof(ModelRow), new PropertyMetadata(null));

    public static readonly DependencyProperty ParametersProperty = DependencyProperty.Register(
        nameof(Parameters), typeof(string), typeof(ModelRow), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ModelRow), new PropertyMetadata(null));

    public static readonly DependencyProperty IsRecommendedProperty = DependencyProperty.Register(
        nameof(IsRecommended), typeof(bool), typeof(ModelRow), new PropertyMetadata(false));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ModelRowState), typeof(ModelRow), new PropertyMetadata(ModelRowState.Available));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ModelRow), new PropertyMetadata(0.0));

    public static readonly RoutedEvent DownloadRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(DownloadRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ModelRow));

    public static readonly RoutedEvent UseRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(UseRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ModelRow));

    static ModelRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ModelRow), new FrameworkPropertyMetadata(typeof(ModelRow)));
        FocusableProperty.OverrideMetadata(typeof(ModelRow), new FrameworkPropertyMetadata(false));
    }

    public event RoutedEventHandler DownloadRequested
    {
        add => AddHandler(DownloadRequestedEvent, value);
        remove => RemoveHandler(DownloadRequestedEvent, value);
    }

    public event RoutedEventHandler UseRequested
    {
        add => AddHandler(UseRequestedEvent, value);
        remove => RemoveHandler(UseRequestedEvent, value);
    }

    public string? ModelName
    {
        get => (string?)GetValue(ModelNameProperty);
        set => SetValue(ModelNameProperty, value);
    }

    /// <summary>The badge text, such as "34M parameters".</summary>
    public string? Parameters
    {
        get => (string?)GetValue(ParametersProperty);
        set => SetValue(ParametersProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsRecommended
    {
        get => (bool)GetValue(IsRecommendedProperty);
        set => SetValue(IsRecommendedProperty, value);
    }

    public ModelRowState State
    {
        get => (ModelRowState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>From 0 to 1, while downloading.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Download") is ButtonBase download)
        {
            AutomationProperties.SetName(download, $"Download {ModelName}");
            download.Click += (_, _) => RaiseEvent(new RoutedEventArgs(DownloadRequestedEvent, this));
        }
        if (GetTemplateChild("PART_Use") is ButtonBase use)
        {
            AutomationProperties.SetName(use, $"Use {ModelName}");
            use.Click += (_, _) => RaiseEvent(new RoutedEventArgs(UseRequestedEvent, this));
        }
    }
}

/// <summary>Square-cornered check box with an accent fill, as in the Transcripts screen.</summary>
public sealed class MacCheckBox : CheckBox
{
    static MacCheckBox() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MacCheckBox), new FrameworkPropertyMetadata(typeof(MacCheckBox)));
}

/// <summary>Round colour choice. The selected swatch gets an accent ring.</summary>
public sealed class ColorSwatch : RadioButton
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Brush), typeof(ColorSwatch), new PropertyMetadata(Brushes.White));

    static ColorSwatch() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ColorSwatch), new FrameworkPropertyMetadata(typeof(ColorSwatch)));

    public Brush Color
    {
        get => (Brush)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
}

/// <summary>Large radio card with a picture, a title and a description.</summary>
public sealed class ChoiceCard : RadioButton
{
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(object), typeof(ChoiceCard), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ChoiceCard),
        new PropertyMetadata(null, (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ChoiceCard), new PropertyMetadata(null));

    static ChoiceCard() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ChoiceCard), new FrameworkPropertyMetadata(typeof(ChoiceCard)));

    public object? Preview
    {
        get => GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}

/// <summary>Quiet icon-and-text line under a card.</summary>
public sealed class FooterNote : Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(FooterNote), new PropertyMetadata(null));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FooterNote), new PropertyMetadata(null));

    static FooterNote()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FooterNote), new FrameworkPropertyMetadata(typeof(FooterNote)));
        FocusableProperty.OverrideMetadata(typeof(FooterNote), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>A single key drawn as a keycap, for shortcuts and inline hints.</summary>
public sealed class Keycap : ContentControl
{
    static Keycap()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Keycap), new FrameworkPropertyMetadata(typeof(Keycap)));
        FocusableProperty.OverrideMetadata(typeof(Keycap), new FrameworkPropertyMetadata(false));
    }
}

/// <summary>The app's logo. Below 56 px it uses the brand kit's simpler drawing, which stays sharp.</summary>
public sealed class AppTile : FrameworkElement
{
    private const double FullLogoMinimum = 56;

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(AppTile),
        new FrameworkPropertyMetadata(36.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawing)
    {
        var key = Size >= FullLogoMinimum ? "Captly.Logo.Tile" : "Captly.Logo.TileSmall";
        if (TryFindResource(key) is ImageSource logo) drawing.DrawImage(logo, new Rect(0, 0, Size, Size));
    }
}