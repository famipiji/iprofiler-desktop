namespace IProfiler.Core.Domain;

/// <summary>A rectangle in original-image pixel coordinates (origin top-left).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
