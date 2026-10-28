using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Storage;
using IProfiler.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IProfiler.Tests.Data;

/// <summary>The SQLite store: Batch → Document → Page metadata round-trips, and no image data ever goes in.</summary>
public sealed class BatchRepositoryTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(
        AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "iProfiler-tests", Guid.NewGuid().ToString("N")));

    private ServiceProvider _services = null!;

    private IBatchRepository Repository => _services.GetRequiredService<IBatchRepository>();

    public async Task InitializeAsync()
    {
        _services = new ServiceCollection().AddLogging().AddSingleton(_paths).AddCaptureDatabase(_paths).BuildServiceProvider();
        await _services.MigrateCaptureDatabaseAsync();
    }

    [Fact]
    public async Task Batch_with_documents_and_page_metadata_round_trips_in_order()
    {
        var batch = SampleBatch();

        await Repository.AddAsync(batch);
        var stored = await Repository.GetAsync(batch.Id);

        Assert.NotNull(stored);
        Assert.Equal(batch.Name, stored.Name);
        Assert.Equal(batch.CreatedAt, stored.CreatedAt);
        Assert.Equal([0, 1], stored.Documents.Select(d => d.Order));
        var expected = batch.Documents.SelectMany(d => d.Pages).ToList();
        var actual = stored.Documents.SelectMany(d => d.Pages).ToList();
        Assert.Equal(expected.Select(Describe), actual.Select(Describe));
    }

    [Fact]
    public async Task Analysis_results_are_saved_on_the_existing_page()
    {
        var batch = SampleBatch();
        await Repository.AddAsync(batch);
        var page = batch.Documents[1].Pages[0];

        page.Rotation = PageRotation.Clockwise270;
        page.DeskewAngle = -2.35;
        page.Crop = null;
        page.Dpi = 200;
        await Repository.UpdatePageAsync(page);

        var stored = (await Repository.GetAsync(batch.Id))!;
        var reloaded = Assert.Single(stored.Documents[1].Pages);
        Assert.Equal(Describe(page), Describe(reloaded));
        Assert.Equal(2, stored.Documents[0].Pages.Count);
    }

    [Fact]
    public async Task Model_matches_the_migrations()
    {
        // Fails when the model changes without `dotnet ef migrations add`.
        await using var db = await _services.GetRequiredService<IDbContextFactory<CaptureDbContext>>().CreateDbContextAsync();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Database_holds_only_metadata()
    {
        // 50 pages pointing at real 300 dpi scans (~60 KB of G4 each): the file stays tiny and has no blob columns.
        var scan = Path.Combine(AppContext.BaseDirectory, "TestData", "barcode", "bw-g4-300dpi.tif");
        var batch = new Batch { Name = "Metadata only" };
        batch.Documents.Add(new Document { Pages = [.. Enumerable.Range(0, 50).Select(i => new Page { ImagePath = scan, Order = i, Dpi = 300 })] });
        await Repository.AddAsync(batch);

        await using var connection = new SqliteConnection($"Data Source={_paths.Database}");
        await connection.OpenAsync();
        var columns = new List<string>();
        foreach (var table in new[] { "Batches", "Documents", "Pages" })
        {
            await using var info = connection.CreateCommand();
            info.CommandText = $"SELECT name, type FROM pragma_table_info('{table}')";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add($"{table}.{reader.GetString(0)} {reader.GetString(1)}");
        }

        Assert.DoesNotContain(columns, c => c.EndsWith(" BLOB", StringComparison.OrdinalIgnoreCase));
        await using var blobs = connection.CreateCommand();
        blobs.CommandText = "SELECT COUNT(*) FROM Pages WHERE typeof(ImagePath) = 'blob' OR length(ImagePath) > 1024";
        Assert.Equal(0L, (long)(await blobs.ExecuteScalarAsync())!);
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
        Assert.True(new FileInfo(_paths.Database).Length < 128 * 1024, $"{new FileInfo(_paths.Database).Length} bytes");
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools(); // release the file so the folder can be deleted
        if (Directory.Exists(_paths.Data))
            Directory.Delete(_paths.Data, recursive: true);
    }

    private static Batch SampleBatch()
    {
        var batch = new Batch { Name = "Invoices 2026-10-28" };
        batch.Documents.Add(new Document
        {
            Order = 0,
            Pages =
            [
                new Page { ImagePath = @"C:\scans\a-1.tif", Order = 0, Rotation = PageRotation.Rotate180, DeskewAngle = 0.5, Dpi = 300, PatchCode = PatchCode.Patch2 },
                new Page { ImagePath = @"C:\scans\a-2.tif", Order = 1, DeskewAngle = null, Crop = new PixelRect(10, 20, 2400, 3400) },
            ],
        });
        batch.Documents.Add(new Document
        {
            Order = 1,
            Pages = [new Page { ImagePath = @"C:\scans\b-1 (kopi).tif", Order = 0, Rotation = PageRotation.Clockwise90, DeskewAngle = -1.25, Crop = new PixelRect(0, 0, 100, 50) }],
        });
        return batch;
    }

    private static string Describe(Page p) =>
        $"{p.Id} {p.ImagePath} #{p.Order} {p.Rotation} deskew={p.DeskewAngle} dpi={p.Dpi} crop={p.Crop} patch={p.PatchCode}";
}
