using System.Diagnostics;
using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using ZXing;
using ZXing.Common;

namespace IProfiler.Infrastructure.Barcodes;

/// <summary>
/// Barcode and QR reading with ZXing.Net on OpenCV images (the same image types as OCR). Full page, or a drawn
/// region ("FlyBCR" / "FlyQR"). Images are read at full resolution: downscaling breaks thin 1D bars.
/// A new reader per call, so calls can run in parallel.
/// </summary>
public sealed class ZXingBarcodeProvider(ILogger<ZXingBarcodeProvider> logger) : IBarcodeProvider
{
    // White margin around a region crop: 1D symbols need a quiet zone, and a drawn region is often tight.
    private const int RegionPadding = 32;

    private static readonly Dictionary<BarcodeFormat, BarcodeSymbology> Formats = new()
    {
        [BarcodeFormat.CODE_39] = BarcodeSymbology.Code39,
        [BarcodeFormat.CODE_128] = BarcodeSymbology.Code128,
        [BarcodeFormat.QR_CODE] = BarcodeSymbology.QrCode,
        [BarcodeFormat.DATA_MATRIX] = BarcodeSymbology.DataMatrix,
        [BarcodeFormat.EAN_13] = BarcodeSymbology.Ean13,
        [BarcodeFormat.PDF_417] = BarcodeSymbology.Pdf417,
    };

    public Task<IReadOnlyList<BarcodeResult>> ReadPageAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(imagePath, region: null), cancellationToken);

    public Task<IReadOnlyList<BarcodeResult>> ReadRegionAsync(string imagePath, PixelRect region, CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(imagePath, region), cancellationToken);

    private IReadOnlyList<BarcodeResult> Read(string imagePath, PixelRect? region)
    {
        var sw = Stopwatch.StartNew();
        using var page = ImageFile.Load(imagePath, ImreadModes.Grayscale);

        var area = ImageFile.Clip(region ?? new PixelRect(0, 0, page.Width, page.Height), page.Width, page.Height);
        if (area.Width == 0 || area.Height == 0)
        {
            logger.LogWarning("Barcode region {Region} lies outside {File} ({Width}x{Height}); nothing to read",
                region, Path.GetFileName(imagePath), page.Width, page.Height);
            return [];
        }

        var padding = region is null ? 0 : RegionPadding;
        using var input = ImageFile.CropWithMargin(page, area, padding);

        var reader = new ZXing.OpenCV.BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [.. Formats.Keys] },
        };
        var found = reader.DecodeMultiple(input) ?? [];

        var results = found
            .Select(r => new BarcodeResult(
                Formats.GetValueOrDefault(r.BarcodeFormat, BarcodeSymbology.Other),
                r.Text,
                Box(r.ResultPoints, padding, area)))
            .Distinct()
            .OrderBy(r => r.Box.Y).ThenBy(r => r.Box.X)
            .ToList();

        logger.LogInformation("Barcodes {Kind} {File}: {Count} found ({Symbologies}) in {ElapsedMs} ms",
            region is null ? "page" : "region", Path.GetFileName(imagePath), results.Count,
            string.Join(", ", results.Select(r => r.Symbology)), sw.ElapsedMilliseconds);
        return results;
    }

    /// <summary>
    /// Bounding box of ZXing's result points, in page pixels. For a QR code that's the finder patterns; for a
    /// 1D symbol ZXing only reports the two ends of the scan line, so the box is that line (height 1).
    /// </summary>
    private static PixelRect Box(ResultPoint[] points, int padding, PixelRect area)
    {
        if (points.Length == 0)
            return area;

        var left = (int)Math.Floor(points.Min(p => p.X)) - padding + area.X;
        var top = (int)Math.Floor(points.Min(p => p.Y)) - padding + area.Y;
        var right = (int)Math.Ceiling(points.Max(p => p.X)) - padding + area.X;
        var bottom = (int)Math.Ceiling(points.Max(p => p.Y)) - padding + area.Y;
        return ImageFile.Clip(new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)),
            area.X + area.Width, area.Y + area.Height);
    }
}
