using IProfiler.Core.Domain;

namespace IProfiler.Core.Scanning;

/// <summary>Imports existing files (TIFF, PDF, JPEG, ...) as pages; a multi-page file yields one page per page.</summary>
public interface IImportProvider
{
    IAsyncEnumerable<Page> ImportAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default);
}
