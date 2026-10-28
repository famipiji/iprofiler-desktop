using IProfiler.Core.Domain;
using IProfiler.Core.Imaging;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Fakes;

public sealed class FakeImageProcessor(ILogger<FakeImageProcessor> logger) : IImageProcessor
{
    public Task<double> DetectSkewAngleAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(0.0);

    public Task<double> GetWhiteRatioAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(FakeBatch.Find(imagePath)?.WhiteRatio ?? 0.85);

    /// <summary>Writes nothing; reports an A4 page at 300 dpi.</summary>
    public Task<ExportGeometry> RenderForExportAsync(Page page, string outputPath, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fake render of {ImagePath} to {OutputPath} skipped", page.ImagePath, outputPath);
        return Task.FromResult(new ExportGeometry(page, 2480, 3508));
    }
}
