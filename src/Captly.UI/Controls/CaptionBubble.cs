using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Captly.Core.Settings;

namespace Captly.UI.Controls;

/// <summary>Text that can carry an outline, which plain WPF text cannot do.</summary>
public sealed class OutlinedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(OutlinedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(OutlinedText), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OutlineProperty = DependencyProperty.Register(
        nameof(Outline), typeof(double), typeof(OutlinedText), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(OutlinedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(OutlinedText),
        new FrameworkPropertyMetadata(22.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits));

    // Matches the design: 30 px between lines at the 22 px default.
    private const double LineHeightRatio = 1.36;

    private FormattedText? _formatted;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double Outline
    {
        get => (double)GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        _formatted = new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            FontSize, Fill, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            LineHeight = FontSize * LineHeightRatio,
        };
        if (!double.IsInfinity(availableSize.Width)) _formatted.MaxTextWidth = Math.Max(1, availableSize.Width);

        // Centred text needs a width to centre within, or it centres around its origin instead.
        var width = Math.Ceiling(_formatted.WidthIncludingTrailingWhitespace);
        _formatted.MaxTextWidth = Math.Max(1, width);
        return new Size(width, _formatted.Height);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (_formatted == null) return;
        _formatted.SetForegroundBrush(Fill);
        var origin = default(Point);
        if (Outline > 0)
        {
            var geometry = _formatted.BuildGeometry(origin);
            drawing.DrawGeometry(null, new Pen(Brushes.Black, Outline * 2) { LineJoin = PenLineJoin.Round }, geometry);
            drawing.DrawGeometry(Fill, null, geometry);
            return;
        }
        drawing.DrawText(_formatted, origin);
    }
}

/// <summary>The overlay's text block, in the four styles of §6.3 #10.</summary>
[TemplatePart(Name = "PART_Box", Type = typeof(Border))]
[TemplatePart(Name = "PART_Text", Type = typeof(OutlinedText))]
public sealed class CaptionBubble : Control
{
    private static readonly Color GlassColor = Color.FromRgb(18, 18, 22);
    private static readonly Brush HighContrastText = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A)));
    private static readonly Brush GlassBorder = Freeze(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CaptionBubble), new PropertyMetadata(""));

    public static readonly DependencyProperty CaptionStyleProperty = DependencyProperty.Register(
        nameof(CaptionStyle), typeof(CaptionStyle), typeof(CaptionBubble), new PropertyMetadata(CaptionStyle.Glass, Restyle));

    public static readonly DependencyProperty TextColorProperty = DependencyProperty.Register(
        nameof(TextColor), typeof(Color), typeof(CaptionBubble), new PropertyMetadata(Colors.White, Restyle));

    public static readonly DependencyProperty BackgroundOpacityProperty = DependencyProperty.Register(
        nameof(BackgroundOpacity), typeof(double), typeof(CaptionBubble), new PropertyMetadata(0.66, Restyle));

    private Border? _box;
    private OutlinedText? _text;

    static CaptionBubble()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CaptionBubble), new FrameworkPropertyMetadata(typeof(CaptionBubble)));
        FocusableProperty.OverrideMetadata(typeof(CaptionBubble), new FrameworkPropertyMetadata(false));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public CaptionStyle CaptionStyle
    {
        get => (CaptionStyle)GetValue(CaptionStyleProperty);
        set => SetValue(CaptionStyleProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public double BackgroundOpacity
    {
        get => (double)GetValue(BackgroundOpacityProperty);
        set => SetValue(BackgroundOpacityProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _box = GetTemplateChild("PART_Box") as Border;
        _text = GetTemplateChild("PART_Text") as OutlinedText;
        Apply();
    }

    private static void Restyle(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CaptionBubble)d).Apply();

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void Apply()
    {
        if (_box == null || _text == null) return;

        _box.BorderThickness = default;
        _box.BorderBrush = null;
        _box.Effect = null;
        _box.CornerRadius = new CornerRadius(12);
        _box.Padding = new Thickness(20, 9, 20, 9);
        _text.Outline = 0;
        _text.Fill = new SolidColorBrush(TextColor);

        switch (CaptionStyle)
        {
            case CaptionStyle.Classic:
                _box.Background = Brushes.Black;
                _box.CornerRadius = new CornerRadius(3);
                break;
            case CaptionStyle.Outline:
                _box.Background = null;
                _text.Outline = 2;
                break;
            case CaptionStyle.HighContrast:
                _box.Background = Brushes.Black;
                _text.Fill = HighContrastText;
                break;
            default:
                _box.Background = new SolidColorBrush(Color.FromArgb((byte)(BackgroundOpacity * 255), GlassColor.R, GlassColor.G, GlassColor.B));
                _box.BorderBrush = GlassBorder;
                _box.BorderThickness = new Thickness(1);
                _box.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black };
                break;
        }
    }
}

/// <summary>Style picker card: an "Aa" chip drawn in that style, with its name below.</summary>
public sealed class StylePresetCard : RadioButton
{
    public static readonly DependencyProperty PresetProperty = DependencyProperty.Register(
        nameof(Preset), typeof(CaptionStyle), typeof(StylePresetCard), new PropertyMetadata(CaptionStyle.Glass));

    static StylePresetCard() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StylePresetCard), new FrameworkPropertyMetadata(typeof(StylePresetCard)));

    public CaptionStyle Preset
    {
        get => (CaptionStyle)GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }
}