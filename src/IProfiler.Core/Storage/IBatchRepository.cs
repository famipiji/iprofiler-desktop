using IProfiler.Core.Domain;

namespace IProfiler.Core.Storage;

/// <summary>
/// Batch → Document → Page metadata in the local database. Only metadata is stored: page images stay as
/// files under <see cref="AppPaths.Scans"/> and are referenced by path (never stored as blobs).
/// </summary>
public interface IBatchRepository
{
    /// <summary>Stores a new batch with all its documents and pages.</summary>
    Task AddAsync(Batch batch, CancellationToken cancellationToken = default);

    /// <summary>The batch with its documents and pages, each in order; null if there is no such batch.</summary>
    Task<Batch?> GetAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>Saves a page's metadata (rotation, deskew, crop, order, resolution) after analysis or an edit.</summary>
    Task UpdatePageAsync(Page page, CancellationToken cancellationToken = default);
}
