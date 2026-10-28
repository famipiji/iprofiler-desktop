using IProfiler.Core.Domain;
using IProfiler.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IProfiler.Data;

/// <summary>One short-lived context per call, so the long-running WPF app never holds a stale change tracker.</summary>
public sealed class EfBatchRepository(IDbContextFactory<CaptureDbContext> contexts, ILogger<EfBatchRepository> logger)
    : IBatchRepository
{
    public async Task AddAsync(Batch batch, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Batches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Stored batch {BatchId} \"{Name}\": {Documents} documents, {Pages} pages",
            batch.Id, batch.Name, batch.Documents.Count, batch.Documents.Sum(d => d.Pages.Count));
    }

    public async Task<Batch?> GetAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Batches
            .AsNoTracking()
            .Include(b => b.Documents.OrderBy(d => d.Order))
            .ThenInclude(d => d.Pages.OrderBy(p => p.Order))
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.Id == batchId, cancellationToken);
    }

    public async Task UpdatePageAsync(Page page, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // Load and copy rather than Attach/Update: the page doesn't carry its DocumentId (a shadow key), and
        // marking it modified would overwrite that with an empty value.
        var stored = await db.Pages.SingleOrDefaultAsync(p => p.Id == page.Id, cancellationToken)
                     ?? throw new InvalidOperationException($"Page {page.Id} is not in the database.");
        stored.Rotation = page.Rotation;
        stored.DeskewAngle = page.DeskewAngle;
        stored.Crop = page.Crop;
        stored.Order = page.Order;
        stored.Dpi = page.Dpi;
        await db.SaveChangesAsync(cancellationToken);
    }
}
