using BitMiracle.LibTiff.Classic;
using IProfiler.Core.Domain;
using OpenCvSharp;

namespace IProfiler.Infrastructure.Imaging;

/// <summary>Image loading and region helpers shared by the OpenCV-based providers.</summary>
internal static class ImageFile
{
    private const double A4ShortSideInches = 8.27;

    // Resolution tags outside this range are treated as missing (some software writes 1 or 72 by default).
    private const double MinPlausibleDpi = 75;
    private const double MaxPlausibleDpi = 2400;

    public static Mat Load(string imagePath, ImreadModes mode)
    {
        // ImDecode instead of ImRead: ImRead can't open non-ASCII paths on Windows.
        var image = Cv2.ImDecode(File.ReadAllBytes(imagePath), mode);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidDataException($"Not an image OpenCV can read: {imagePath}");
        }

        return image;
    }

    /// <summary>
    /// The page's resolution: <paramref name="recorded"/> (from the scan request) if known, else the TIFF's own
    /// resolution tag, else an estimate from the short side (A4 8.27"; Letter is within 3%), because OpenCV
    /// doesn't expose the resolution and JPEG/PNG files are only estimated for now.
    /// </summary>
    public static double Dpi(string imagePath, int width, int height, int? recorded = null) =>
        recorded ?? TiffDpi(imagePath) ?? Math.Min(width, height) / A4ShortSideInches;

    /// <summary>Horizontal resolution from a TIFF's XResolution/ResolutionUnit tags; null if not a TIFF or not tagged.</summary>
    public static double? TiffDpi(string imagePath)
    {
        if (!IsTiff(imagePath))
            return null;

        TiffMessages.Install();
        using var tiff = Tiff.Open(imagePath, "r");
        var x = tiff?.GetField(TiffTag.XRESOLUTION);
        if (x is null)
            return null;

        var unit = (ResUnit)(tiff!.GetField(TiffTag.RESOLUTIONUNIT)?[0].ToInt() ?? (int)ResUnit.INCH);
        var dpi = unit switch
        {
            ResUnit.INCH => x[0].ToDouble(),
            ResUnit.CENTIMETER => x[0].ToDouble() * 2.54,
            _ => 0,
        };
        return dpi is >= MinPlausibleDpi and <= MaxPlausibleDpi ? dpi : null;
    }

    private static bool IsTiff(string imagePath)
    {
        Span<byte> header = stackalloc byte[4];
        using var file = File.OpenRead(imagePath);
        // "II*\0" (little-endian) or "MM\0*" (big-endian).
        return file.Read(header) == 4 && (header.SequenceEqual((byte[])[0x49, 0x49, 0x2A, 0x00]) || header.SequenceEqual((byte[])[0x4D, 0x4D, 0x00, 0x2A]));
    }

    /// <summary>The part of <paramref name="r"/> inside a <paramref name="width"/> x <paramref name="height"/> image (may be empty).</summary>
    public static PixelRect Clip(PixelRect r, int width, int height) => r.ClipTo(width, height);

    /// <summary>A copy of <paramref name="area"/> with a white margin of <paramref name="padding"/> pixels on every side.</summary>
    public static Mat CropWithMargin(Mat page, PixelRect area, int padding)
    {
        using var crop = new Mat(page, new Rect(area.X, area.Y, area.Width, area.Height));
        var padded = new Mat();
        Cv2.CopyMakeBorder(crop, padded, padding, padding, padding, padding, BorderTypes.Constant, Scalar.All(255));
        return padded;
    }
}
