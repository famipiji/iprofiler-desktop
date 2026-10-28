using IProfiler.Core.Domain;
using IProfiler.Core.Export;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Writes nothing; only logs what it would have assembled.</summary>
public sealed class FakeTiffAssembler(ILogger<FakeTiffAssembler> logger) : ITiffAssembler
{
    public Task CreateMultiPageTiffAsync(IReadOnlyList<Page> pages, string outputPath, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fake TIFF of {PageCount} pages to {OutputPath} skipped", pages.Count, outputPath);
        return Task.CompletedTask;
    }
}
