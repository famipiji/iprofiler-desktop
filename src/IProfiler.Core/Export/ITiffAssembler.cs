using IProfiler.Core.Domain;

namespace IProfiler.Core.Export;

public interface ITiffAssembler
{
    /// <summary>Writes one multi-page TIFF with the pages in order, rotation and crop applied.</summary>
    Task CreateMultiPageTiffAsync(IReadOnlyList<Page> pages, string outputPath, CancellationToken cancellationToken = default);
}
