using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Captly.UI.Controls;

/// <summary>Pill toggle. As a ToggleButton it already toggles with Space and exposes the Toggle pattern.</summary>
[TemplatePart(Name = "PART_Fill", Type = typeof(UIElement))]
[TemplatePart(Name = "PART_Knob", Type = typeof(FrameworkElement))]
public sealed class ToggleSwitch : ToggleButton
{
    private UIElement? _fill;
    private TranslateTransform? _knob;

    static ToggleSwitch() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ToggleSwitch), new FrameworkPropertyMetadata(typeof(ToggleSwitch)));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _fill = GetTemplateChild("PART_Fill") as UIElement;
        if (GetTemplateChild("PART_Knob") is FrameworkElement knob)
        {
            knob.Width = Height - 4;
            knob.RenderTransform = _knob = new TranslateTransform();
        }
        Show(animated: false);
    }

    // Only a switch on screen slides. One set by a binding as its window opens takes its place at once.
    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        Show(animated: IsLoaded);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        Show(animated: IsLoaded);
    }

    private void Show(bool animated)
    {
        if (_fill == null || _knob == null) return;
        var on = IsChecked == true;
        var opacity = on ? 1.0 : 0.0;
        var x = on ? Width - Height : 0;
        if (!animated || Motion.Fast.TimeSpan == TimeSpan.Zero)
        {
            _fill.BeginAnimation(OpacityProperty, null);
            _fill.Opacity = opacity;
            _knob.BeginAnimation(TranslateTransform.XProperty, null);
            _knob.X = x;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _fill.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, Motion.Fast) { EasingFunction = ease });
        _knob.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, Motion.Fast) { EasingFunction = ease });
    }
}

/// <summary>A single-choice row of segments. Arrow keys move the selection, as radio buttons do.</summary>
public sealed class SegmentedControl : ListBox
{
    static SegmentedControl() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SegmentedControl), new FrameworkPropertyMetadata(typeof(SegmentedControl)));

    protected override DependencyObject GetContainerForItemOverride() => new SegmentedItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is SegmentedItem;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0,
        };
        if (step == 0)
        {
            base.OnKeyDown(e);
            return;
        }
        Move(step);
        e.Handled = true;
    }

    internal void Move(int step)
    {
        var index = Math.Max(SelectedIndex, 0);
        for (var tried = 0; tried < Items.Count; tried++)
        {
            index = (index + step + Items.Count) % Items.Count;
            if (ItemContainerGenerator.ContainerFromIndex(index) is not UIElement { IsEnabled: false }) break;
        }
        SelectedIndex = index;
        (ItemContainerGenerator.ContainerFromIndex(index) as UIElement)?.Focus();
    }
}

public sealed class SegmentedItem : ListBoxItem
{
    static SegmentedItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SegmentedItem), new FrameworkPropertyMetadata(typeof(SegmentedItem)));
}

/// <summary>ComboBox styled as a compact pop-up button.</summary>
public sealed class MacSelect : ComboBox
{
    static MacSelect() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MacSelect), new FrameworkPropertyMetadata(typeof(MacSelect)));

    protected override DependencyObject GetContainerForItemOverride() => new MacSelectItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is MacSelectItem;
}

public sealed class MacSelectItem : ComboBoxItem
{
    static MacSelectItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MacSelectItem), new FrameworkPropertyMetadata(typeof(MacSelectItem)));
}

/// <summary>Thin track with an accent fill. Optional "A … A" end labels and value text.</summary>
public sealed class MacSlider : Slider
{
    public static readonly DependencyProperty ShowSizeLabelsProperty = DependencyProperty.Register(
        nameof(ShowSizeLabels), typeof(bool), typeof(MacSlider), new PropertyMetadata(false));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(MacSlider), new PropertyMetadata(null, (d, _) => ((MacSlider)d).UpdateValueText()));

    private static readonly DependencyPropertyKey ValueTextKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueText), typeof(string), typeof(MacSlider), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueTextProperty = ValueTextKey.DependencyProperty;

    static MacSlider() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MacSlider), new FrameworkPropertyMetadata(typeof(MacSlider)));

    public bool ShowSizeLabels
    {
        get => (bool)GetValue(ShowSizeLabelsProperty);
        set => SetValue(ShowSizeLabelsProperty, value);
    }

    /// <summary>A format string such as "{0:0} %". Null hides the value text.</summary>
    public string? ValueFormat
    {
        get => (string?)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public string ValueText => (string)GetValue(ValueTextProperty);

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateValueText();
    }

    private void UpdateValueText() =>
        SetValue(ValueTextKey, ValueFormat == null ? "" : string.Format(CultureInfo.CurrentCulture, ValueFormat, Value));
}
