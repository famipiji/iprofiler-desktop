using System.Diagnostics;
using IProfiler.Core.Domain;
using IProfiler.Core.Imaging;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace IProfiler.Infrastructure.Imaging;

/// <summary>
/// Deskew-angle detection, blank-page white ratio and export rendering with OpenCV. Analysis only reads the
/// page image; rotation, deskew and crop are applied to a copy at export time (CLAUDE.md rule 7).
/// </summary>
public sealed class OpenCvImageProcessor(ILogger<OpenCvImageProcessor> logger) : IImageProcessor
{
    /// <summary>Pixels darker than this count as ink (bilevel scans are 0/255; grey paper is ~230).</summary>
    public const int InkThreshold = 128;

    /// <summary>Largest skew searched, either way.</summary>
    public const double MaxSkewDegrees = 10;

    // Analysis runs on a copy downscaled to this longest side (same as OCR); fast, and precise to ~0.05 deg.
    private const int AnalysisLongSide = 1600;

    // Ignored band along every edge (fraction of width/height): scanner edge shadows and punch holes.
    private const double EdgeMargin = 0.02;

    // Ink blobs smaller than this many square inches are scanner specks (~0.012" across).
    private const double SpeckSquareInches = 0.012 * 0.012;

    // Pages with less ink than this fraction have no lines to measure skew from.
    private const double MinInkForSkew = 0.0005;

    public Task<double> DetectSkewAngleAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => DetectSkewAngle(imagePath, cancellationToken), cancellationToken);

    public Task<double> GetWhiteRatioAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => GetWhiteRatio(imagePath), cancellationToken);

    public Task<ExportGeometry> RenderForExportAsync(Page page, string outputPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Render(page, outputPath), cancellationToken);

    /// <summary>
    /// Projection-profile search: the rotation at which the rows of ink are sharpest (text lines line up with
    /// pixel rows) is the straight one. Coarse 0.5 deg steps, then 0.05 deg around the best.
    /// </summary>
    private double DetectSkewAngle(string imagePath, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var gray = ImageFile.Load(imagePath, ImreadModes.Grayscale);
        using var small = Downscale(gray);
        using var ink = InkMask(small, out var content);

        var inkFraction = (double)Cv2.CountNonZero(ink) / (content.Width * content.Height);
        if (inkFraction < MinInkForSkew)
        {
            logger.LogInformation("Skew {File}: too little ink ({InkPercent:F3}%), reported as 0", Path.GetFileName(imagePath), inkFraction * 100);
            return 0;
        }

        var best = Search(ink, -MaxSkewDegrees, MaxSkewDegrees, 0.5, cancellationToken);
        best = Search(ink, best - 0.5, best + 0.5, 0.05, cancellationToken);

        // OpenCV angles are counter-clockwise; IImageProcessor reports the clockwise correction.
        var correction = Math.Round(-best, 2);
        logger.LogInformation("Skew {File}: correction {Angle:F2} deg clockwise in {ElapsedMs} ms",
            Path.GetFileName(imagePath), correction, sw.ElapsedMilliseconds);
        return correction;
    }

    private static double Search(Mat ink, double from, double to, double step, CancellationToken cancellationToken)
    {
        var center = new Point2f(ink.Width / 2f, ink.Height / 2f);
        var bestAngle = 0.0;
        var bestScore = double.MinValue;
        using var rotated = new Mat();
        using var rows = new Mat();
        for (var angle = from; angle <= to + step / 2; angle += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var m = Cv2.GetRotationMatrix2D(center, angle, 1.0);
            Cv2.WarpAffine(ink, rotated, m, ink.Size(), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));
            Cv2.Reduce(rotated, rows, ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_64F);

            // Sum of squared differences between neighbouring rows: highest when lines are horizontal.
            var score = 0.0;
            for (var y = 1; y < rows.Rows; y++)
            {
                var d = rows.At<double>(y, 0) - rows.At<double>(y - 1, 0);
                score += d * d;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        return bestAngle;
    }

    /// <summary>
    /// White fraction of the page inside the edge margin, after dropping scanner specks. A staple and its
    /// shadow are a small, solid blob, so a stapled blank sheet still scores above 0.99.
    /// </summary>
    private double GetWhiteRatio(string imagePath)
    {
        var sw = Stopwatch.StartNew();
        using var gray = ImageFile.Load(imagePath, ImreadModes.Grayscale);
        using var ink = InkMask(gray, out var content);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var count = Cv2.ConnectedComponentsWithStats(ink, labels, stats, centroids, PixelConnectivity.Connectivity8);

        var dpi = ImageFile.Dpi(imagePath, gray.Width, gray.Height);
        var minArea = Math.Max(2, (int)(SpeckSquareInches * dpi * dpi));
        long inkPixels = 0;
        var specks = 0;
        for (var i = 1; i < count; i++) // 0 is the background
        {
            var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            if (area >= minArea)
                inkPixels += area;
            else
                specks++;
        }

        var ratio = 1.0 - (double)inkPixels / ((long)content.Width * content.Height);
        logger.LogInformation("White ratio {File}: {Ratio:F5} ({InkPixels} ink px, {Specks} specks ignored at {Dpi:F0} dpi) in {ElapsedMs} ms",
            Path.GetFileName(imagePath), ratio, inkPixels, specks, dpi, sw.ElapsedMilliseconds);
        return ratio;
    }

    /// <summary>
    /// The steps of <see cref="ExportGeometry"/>: crop (original pixels) → deskew → quarter-turn rotation. Writes a
    /// new file; the original is only read.
    /// </summary>
    private ExportGeometry Render(Page page, string outputPath)
    {
        if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(page.ImagePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export must not overwrite the original page image.", nameof(outputPath));

        var sw = Stopwatch.StartNew();
        using var original = ImageFile.Load(page.ImagePath, ImreadModes.Unchanged);
        var geometry = new ExportGeometry(page, original.Width, original.Height);
        var area = geometry.Area;

        using var cropped = new Mat(original, new Rect(area.X, area.Y, area.Width, area.Height));
        using var deskewed = Deskew(cropped, geometry.DeskewDegrees);
        using var output = new Mat();
        if (geometry.Rotation == PageRotation.None)
            deskewed.CopyTo(output);
        else
            Cv2.Rotate(deskewed, output, geometry.Rotation switch
            {
                PageRotation.Clockwise90 => RotateFlags.Rotate90Clockwise,
                PageRotation.Rotate180 => RotateFlags.Rotate180,
                PageRotation.Clockwise270 => RotateFlags.Rotate90Counterclockwise,
                _ => throw new ArgumentOutOfRangeException(nameof(page), page.Rotation, "Unknown rotation."),
            });

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        // ImEncode + WriteAllBytes instead of ImWrite: ImWrite can't handle non-ASCII paths on Windows.
        File.WriteAllBytes(outputPath, output.ImEncode(Path.GetExtension(outputPath)));

        logger.LogInformation("Rendered {File} → {Output} ({Width}x{Height}; crop {Crop}, deskew {Deskew} deg, rotation {Rotation}) in {ElapsedMs} ms",
            Path.GetFileName(page.ImagePath), outputPath, output.Width, output.Height, page.Crop, geometry.DeskewDegrees, page.Rotation, sw.ElapsedMilliseconds);
        return geometry;
    }

    private static Mat Deskew(Mat image, double clockwiseDegrees)
    {
        if (clockwiseDegrees == 0)
            return image.Clone();

        // Bilevel scans stay bilevel (nearest neighbour); greyscale and colour get smooth edges.
        var bilevel = image.Channels() == 1 && IsBilevel(image);
        var center = new Point2f(image.Width / 2f, image.Height / 2f);
        using var m = Cv2.GetRotationMatrix2D(center, -clockwiseDegrees, 1.0);
        var result = new Mat();
        Cv2.WarpAffine(image, result, m, image.Size(),
            bilevel ? InterpolationFlags.Nearest : InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(255));
        return result;
    }

    private static bool IsBilevel(Mat gray)
    {
        using var midTones = new Mat();
        Cv2.InRange(gray, new Scalar(1), new Scalar(254), midTones);
        return Cv2.CountNonZero(midTones) == 0;
    }

    private static Mat Downscale(Mat gray)
    {
        var scale = Math.Min(1.0, (double)AnalysisLongSide / Math.Max(gray.Width, gray.Height));
        var result = new Mat();
        if (scale < 1.0)
            Cv2.Resize(gray, result, new Size(), scale, scale, InterpolationFlags.Area);
        else
            gray.CopyTo(result);
        return result;
    }

    /// <summary>255 where there is ink, 0 elsewhere; the edge margin is blanked out and returned as <paramref name="content"/>.</summary>
    private static Mat InkMask(Mat gray, out Rect content)
    {
        var ink = new Mat();
        Cv2.Threshold(gray, ink, InkThreshold - 1, 255, ThresholdTypes.BinaryInv);
        var mx = (int)(gray.Width * EdgeMargin);
        var my = (int)(gray.Height * EdgeMargin);
        content = new Rect(mx, my, gray.Width - 2 * mx, gray.Height - 2 * my);
        using var mask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.All(0));
        using (var inside = new Mat(mask, content))
            inside.SetTo(Scalar.All(255));
        Cv2.BitwiseAnd(ink, mask, ink);
        return ink;
    }
}
