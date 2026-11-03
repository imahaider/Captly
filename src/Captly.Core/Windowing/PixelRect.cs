namespace Captly.Core.Windowing;

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>A rectangle in physical screen pixels. Right and Bottom are exclusive, as in Win32.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;
    public PixelPoint Center => new(Left + Width / 2, Top + Height / 2);

    public static PixelRect FromSize(int left, int top, int width, int height) => new(left, top, left + width, top + height);

    public bool Contains(PixelPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Contains(PixelRect other) => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>The overlap, or an empty rectangle when there is none.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var overlap = new PixelRect(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return overlap.IsEmpty ? default : overlap;
    }
}

/// <summary>A display. Scale is its DPI over 96, so 1.25 at 125 %.</summary>
public sealed record MonitorInfo(PixelRect Bounds, PixelRect WorkArea, double Scale);
