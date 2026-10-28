using System.Diagnostics;
using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace IProfiler.Infrastructure.Barcodes;

/// <summary>
/// Software patch-code check (the fallback when the scanner doesn't report patch codes through TWAIN).
/// A patch code is four solid bars, two wide (0.20") and two narrow (0.08"), with narrow (0.08") spaces;
/// the wide/narrow order gives the code. ZXing doesn't read patch codes.
/// </summary>
/// <remarks>
/// Spike-level prototype:
/// - Resolution comes from the TIFF's resolution tag; other formats are estimated from the page's short side.
/// - Vertical bars are read left to right and horizontal bars top to bottom. On a sheet fed upside down
///   the order reverses, which turns Patch 1 into Patch 6 and Patch 3 into Patch T. Page orientation
///   (Task 1.4) has to be known first, or the scanner's own detection used.
/// </remarks>
public sealed class OpenCvPatchCodeDetector(ILogger<OpenCvPatchCodeDetector> logger) : IPatchCodeDetector
{
    private const double NarrowInches = 0.08;
    private const double WideInches = 0.20;

    private static readonly Dictionary<string, PatchCode> Patterns = new()
    {
        ["WWNN"] = PatchCode.Patch1,
        ["WNNW"] = PatchCode.Patch2,
        ["WNWN"] = PatchCode.Patch3,
        ["NWWN"] = PatchCode.Patch4,
        ["NNWW"] = PatchCode.Patch6,
        ["NWNW"] = PatchCode.PatchT,
    };

    public Task<PatchCode?> DetectAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(imagePath), cancellationToken);

    private PatchCode? Detect(string imagePath)
    {
        var sw = Stopwatch.StartNew();
        using var page = ImageFile.Load(imagePath, ImreadModes.Grayscale);
        var dpi = ImageFile.Dpi(imagePath, page.Width, page.Height);

        using var ink = new Mat();
        Cv2.Threshold(page, ink, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Cv2.FindContours(ink, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var bars = contours
            .Select(c => (Rect: Cv2.BoundingRect(c), Area: Cv2.ContourArea(c)))
            .Where(c => IsBar(c.Rect, c.Area, dpi))
            .Select(c => c.Rect)
            .ToList();

        // Vertical bars, read along x; horizontal bars, read along y.
        var code = Find(bars.Where(b => b.Height > b.Width).Select(b => new Bar(b.X, b.Width, b.Y, b.Bottom)), dpi)
                   ?? Find(bars.Where(b => b.Width > b.Height).Select(b => new Bar(b.Y, b.Height, b.X, b.Right)), dpi);

        logger.LogInformation("Patch code {File}: {PatchCode} ({Bars} bar candidates, {Dpi:F0} dpi) in {ElapsedMs} ms",
            Path.GetFileName(imagePath), code?.ToString() ?? "none", bars.Count, dpi, sw.ElapsedMilliseconds);
        return code;
    }

    /// <summary>A solid, long rectangle as thick as a narrow-to-wide patch bar (with print/scan tolerance).</summary>
    private static bool IsBar(Rect r, double area, double dpi)
    {
        var thickness = Math.Min(r.Width, r.Height);
        var length = Math.Max(r.Width, r.Height);
        return area >= 0.8 * r.Width * r.Height
               && thickness >= 0.05 * dpi && thickness <= 0.30 * dpi
               && length >= 0.5 * dpi && length >= 3 * thickness;
    }

    /// <summary>Four bars in a row with narrow spaces and overlapping extents, and no fifth bar next to them.</summary>
    private static PatchCode? Find(IEnumerable<Bar> candidates, double dpi)
    {
        var bars = candidates.OrderBy(b => b.Start).ToList();
        for (var i = 0; i + 4 <= bars.Count; i++)
        {
            var group = bars.GetRange(i, 4);
            if (!Enumerable.Range(0, 3).All(k => Adjacent(group[k], group[k + 1], dpi)))
                continue;
            if (i > 0 && Adjacent(bars[i - 1], group[0], dpi))
                continue;
            if (i + 4 < bars.Count && Adjacent(group[3], bars[i + 4], dpi))
                continue;

            var threshold = (NarrowInches + WideInches) / 2 * dpi;
            var pattern = string.Concat(group.Select(b => b.Thickness > threshold ? 'W' : 'N'));
            if (Patterns.TryGetValue(pattern, out var code))
                return code;
        }

        return null;
    }

    private static bool Adjacent(Bar a, Bar b, double dpi)
    {
        var gap = b.Start - (a.Start + a.Thickness);
        var overlap = Math.Min(a.SpanEnd, b.SpanEnd) - Math.Max(a.SpanStart, b.SpanStart);
        var shorter = Math.Min(a.SpanEnd - a.SpanStart, b.SpanEnd - b.SpanStart);
        return gap >= 0.04 * dpi && gap <= 0.14 * dpi && overlap >= 0.8 * shorter;
    }

    /// <param name="Start">Position along the reading direction.</param>
    /// <param name="Thickness">Bar width along the reading direction.</param>
    /// <param name="SpanStart">Where the bar starts across the reading direction.</param>
    /// <param name="SpanEnd">Where the bar ends across the reading direction.</param>
    private readonly record struct Bar(int Start, int Thickness, int SpanStart, int SpanEnd);
}
