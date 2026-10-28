using IProfiler.Core.Domain;

namespace IProfiler.Core.Imaging;

/// <summary>
/// The one mapping between original-image pixels and export-image pixels for a page: crop (original pixels)
/// → deskew about the crop's centre (same size, corners filled white) → quarter-turn rotation. The export
/// renderer applies exactly this, and everything drawn over the export (OCR text, highlights, the viewer's
/// crop handles) maps through it rather than doing its own maths. Coordinates are continuous: (0,0) is the
/// top-left corner of the top-left pixel.
/// </summary>
public sealed class ExportGeometry
{
    /// <summary>Skew corrections smaller than this are skipped (the export is a plain copy).</summary>
    public const double MinDeskewDegrees = 0.01;

    private readonly double _cos;
    private readonly double _sin;
    private readonly double _cx;
    private readonly double _cy;

    public ExportGeometry(Page page, int originalWidth, int originalHeight)
    {
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
        Area = (page.Crop ?? new PixelRect(0, 0, originalWidth, originalHeight)).ClipTo(originalWidth, originalHeight);
        if (Area.Width == 0 || Area.Height == 0)
            throw new InvalidOperationException($"Crop {page.Crop} lies outside the {originalWidth}x{originalHeight} image {page.ImagePath}.");

        DeskewDegrees = Math.Abs(page.DeskewAngle ?? 0) < MinDeskewDegrees ? 0 : page.DeskewAngle!.Value;
        Rotation = page.Rotation;
        (Width, Height) = Rotation is PageRotation.Clockwise90 or PageRotation.Clockwise270
            ? (Area.Height, Area.Width)
            : (Area.Width, Area.Height);

        // A clockwise correction on screen (y down) is the rotation matrix [cos sin; -sin cos] about the centre.
        var radians = -DeskewDegrees * Math.PI / 180;
        _cos = Math.Cos(radians);
        _sin = Math.Sin(radians);
        _cx = Area.Width / 2.0;
        _cy = Area.Height / 2.0;
    }

    public int OriginalWidth { get; }

    public int OriginalHeight { get; }

    /// <summary>The crop actually applied: the page's crop clipped to the image, or the whole image.</summary>
    public PixelRect Area { get; }

    /// <summary>Clockwise skew correction applied, in degrees (0 when below <see cref="MinDeskewDegrees"/>).</summary>
    public double DeskewDegrees { get; }

    public PageRotation Rotation { get; }

    /// <summary>Width of the export image in pixels.</summary>
    public int Width { get; }

    /// <summary>Height of the export image in pixels.</summary>
    public int Height { get; }

    public (double X, double Y) ToExport(double x, double y)
    {
        // Crop, then deskew about the crop's centre.
        var dx = x - Area.X - _cx;
        var dy = y - Area.Y - _cy;
        var sx = _cos * dx + _sin * dy + _cx;
        var sy = -_sin * dx + _cos * dy + _cy;

        return Rotation switch
        {
            PageRotation.None => (sx, sy),
            PageRotation.Clockwise90 => (Area.Height - sy, sx),
            PageRotation.Rotate180 => (Area.Width - sx, Area.Height - sy),
            PageRotation.Clockwise270 => (sy, Area.Width - sx),
            _ => throw new InvalidOperationException($"Unknown rotation {Rotation}."),
        };
    }

    public (double X, double Y) ToOriginal(double x, double y)
    {
        var (sx, sy) = Rotation switch
        {
            PageRotation.None => (x, y),
            PageRotation.Clockwise90 => (y, Area.Height - x),
            PageRotation.Rotate180 => (Area.Width - x, Area.Height - y),
            PageRotation.Clockwise270 => (Area.Width - y, x),
            _ => throw new InvalidOperationException($"Unknown rotation {Rotation}."),
        };

        var dx = sx - _cx;
        var dy = sy - _cy;
        return (_cos * dx - _sin * dy + _cx + Area.X, _sin * dx + _cos * dy + _cy + Area.Y);
    }

    /// <summary>
    /// An original-image box (e.g. an OCR word) in export pixels: its centre is mapped, and its sides swap on a
    /// quarter turn. The deskew rotates the box with the text, so it stays axis-aligned in the export.
    /// </summary>
    public PixelRect ToExport(PixelRect box)
    {
        var (x, y) = ToExport(box.X + box.Width / 2.0, box.Y + box.Height / 2.0);
        var (w, h) = Rotation is PageRotation.Clockwise90 or PageRotation.Clockwise270
            ? (box.Height, box.Width)
            : (box.Width, box.Height);
        return new PixelRect((int)Math.Round(x - w / 2.0), (int)Math.Round(y - h / 2.0), w, h);
    }

    /// <summary>The inverse of <see cref="ToExport(PixelRect)"/>: an export-image box in original pixels.</summary>
    public PixelRect ToOriginal(PixelRect box)
    {
        var (x, y) = ToOriginal(box.X + box.Width / 2.0, box.Y + box.Height / 2.0);
        var (w, h) = Rotation is PageRotation.Clockwise90 or PageRotation.Clockwise270
            ? (box.Height, box.Width)
            : (box.Width, box.Height);
        return new PixelRect((int)Math.Round(x - w / 2.0), (int)Math.Round(y - h / 2.0), w, h);
    }
}
