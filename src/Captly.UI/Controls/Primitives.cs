using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Captly.UI.Controls;

/// <summary>Lets one button template carry different corner radii.</summary>
public static class Shape
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Shape), new PropertyMetadata(default(CornerRadius)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
}

/// <summary>A Lucide line icon, drawn on its 24 px grid with a round 2 px stroke.</summary>
public sealed class Icon : FrameworkElement
{
    private const double Grid = 24;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawing)
    {
        if (Data == null) return;
        var pen = new Pen(Foreground, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawing.PushTransform(new ScaleTransform(Size / Grid, Size / Grid));
        drawing.DrawGeometry(null, pen, Data);
        drawing.Pop();
    }
}

public enum BadgeVariant { Neutral, Success, Info }

/// <summary>11 SemiBold pill, in one of the token colour pairs.</summary>
public sealed class Badge : ContentControl
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(BadgeVariant), typeof(Badge), new PropertyMetadata(BadgeVariant.Neutral));

    static Badge() => DefaultStyleKeyProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(typeof(Badge)));

    public BadgeVariant Variant
    {
        get => (BadgeVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
}

/// <summary>Determinate ring for model downloads.</summary>
public sealed class ProgressRing : FrameworkElement
{
    private const double Stroke = 2.5;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(ProgressRing), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(ProgressRing), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>From 0 to 1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var radius = (Math.Min(ActualWidth, ActualHeight) - Stroke) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawing.DrawEllipse(null, new Pen(Track, Stroke), center, radius, radius);

        var value = Math.Clamp(Value, 0, 0.9999);
        if (value <= 0) return;
        var angle = value * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, value > 0.5, SweepDirection.Clockwise, true, false);
        }
        drawing.DrawGeometry(null, new Pen(Foreground, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
    }
}

/// <summary>
/// Five bars driven by a session's peak level. Lit bars stand at their full height in green.
/// The rest shrink to grey dots, so a silent app shows a row of dots.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    private const double BarWidth = 3;
    private const double Gap = 2;
    private const double DotHeight = 4;
    private const double MeterHeight = 18;
    private static readonly double[] Heights = [0.5, 0.75, 1, 0.65, 0.45];

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(Brush), typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.Green, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IdleProperty = DependencyProperty.Register(
        nameof(Idle), typeof(Brush), typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public Brush Active
    {
        get => (Brush)GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    public Brush Idle
    {
        get => (Brush)GetValue(IdleProperty);
        set => SetValue(IdleProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Heights.Length * BarWidth + (Heights.Length - 1) * Gap, MeterHeight);

    protected override void OnRender(DrawingContext drawing)
    {
        var lit = (int)Math.Ceiling(Math.Clamp(Level, 0, 1) * Heights.Length);
        for (var i = 0; i < Heights.Length; i++)
        {
            var height = i < lit ? MeterHeight * Heights[i] : DotHeight;
            var bar = new Rect(i * (BarWidth + Gap), MeterHeight - height, BarWidth, height);
            drawing.DrawRoundedRectangle(i < lit ? Active : Idle, null, bar, 1.5, 1.5);
        }
    }
}