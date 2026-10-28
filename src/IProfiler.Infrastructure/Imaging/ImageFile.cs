using IProfiler.Core.Domain;
using OpenCvSharp;

namespace IProfiler.Infrastructure.Imaging;

/// <summary>Image loading and region helpers shared by the OpenCV-based providers.</summary>
internal static class ImageFile
{
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

    /// <summary>The part of <paramref name="r"/> inside a <paramref name="width"/> x <paramref name="height"/> image (may be empty).</summary>
    public static PixelRect Clip(PixelRect r, int width, int height)
    {
        var left = Math.Clamp(r.X, 0, width);
        var top = Math.Clamp(r.Y, 0, height);
        var right = Math.Clamp(r.X + r.Width, left, width);
        var bottom = Math.Clamp(r.Y + r.Height, top, height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>A copy of <paramref name="area"/> with a white margin of <paramref name="padding"/> pixels on every side.</summary>
    public static Mat CropWithMargin(Mat page, PixelRect area, int padding)
    {
        using var crop = new Mat(page, new Rect(area.X, area.Y, area.Width, area.Height));
        var padded = new Mat();
        Cv2.CopyMakeBorder(crop, padded, padding, padding, padding, padding, BorderTypes.Constant, Scalar.All(255));
        return padded;
    }
}
