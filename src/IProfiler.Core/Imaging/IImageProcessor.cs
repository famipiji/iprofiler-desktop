using IProfiler.Core.Domain;

namespace IProfiler.Core.Imaging;

/// <summary>Image analysis and export rendering. Never modifies the original page image.</summary>
public interface IImageProcessor
{
    /// <summary>Skew angle in degrees (positive = clockwise) that would straighten the page.</summary>
    Task<double> DetectSkewAngleAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>Fraction of white pixels on the page, 0..1 (1 = completely white).</summary>
    Task<double> GetWhiteRatioAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a copy of the page with its crop, deskew and rotation applied to <paramref name="outputPath"/> (format
    /// from the extension). Returns the mapping it applied, so overlays such as OCR text land on the same pixels.
    /// </summary>
    Task<ExportGeometry> RenderForExportAsync(Page page, string outputPath, CancellationToken cancellationToken = default);
}
