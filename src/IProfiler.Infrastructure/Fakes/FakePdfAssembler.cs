using IProfiler.Core.Export;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Writes nothing; only logs what it would have assembled.</summary>
public sealed class FakePdfAssembler(ILogger<FakePdfAssembler> logger) : IPdfAssembler
{
    public Task CreatePdfAsync(IReadOnlyList<ExportPage> pages, string outputPath, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fake PDF of {PageCount} pages to {OutputPath} skipped", pages.Count, outputPath);
        return Task.CompletedTask;
    }
}
