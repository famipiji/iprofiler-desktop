namespace IProfiler.Core.Domain;

/// <summary>A rectangle in original-image pixel coordinates (origin top-left).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>The part of this rectangle inside a <paramref name="width"/> x <paramref name="height"/> image (may be empty).</summary>
    public PixelRect ClipTo(int width, int height)
    {
        var left = Math.Clamp(X, 0, width);
        var top = Math.Clamp(Y, 0, height);
        var right = Math.Clamp(X + Width, left, width);
        var bottom = Math.Clamp(Y + Height, top, height);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}
