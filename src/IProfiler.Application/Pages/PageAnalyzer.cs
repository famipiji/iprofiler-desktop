using IProfiler.Core.Domain;
using IProfiler.Core.Imaging;
using IProfiler.Core.Recognition;
using Microsoft.Extensions.Logging;

namespace IProfiler.Application.Pages;

/// <summary>
/// Sets a new page's orientation and skew metadata. Skew is measured on the original image, and a 180 degree
/// turn doesn't change it, so both are independent and are applied together on export.
/// </summary>
public sealed class PageAnalyzer(IOcrProvider ocr, IImageProcessor images, ILogger<PageAnalyzer> logger) : IPageAnalyzer
{
    public async Task AnalyzeAsync(Page page, CancellationToken cancellationToken = default)
    {
        var orientation = await ocr.DetectOrientationAsync(page.ImagePath, cancellationToken);
        page.Rotation = orientation.Rotation;
        page.DeskewAngle = await images.DetectSkewAngleAsync(page.ImagePath, cancellationToken);

        logger.LogInformation("Page {File}: rotation {Rotation}, deskew {DeskewAngle:F2} deg",
            Path.GetFileName(page.ImagePath), page.Rotation, page.DeskewAngle);
    }
}
