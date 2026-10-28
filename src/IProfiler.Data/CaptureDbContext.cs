using IProfiler.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IProfiler.Data;

/// <summary>
/// Batch → Document → Page metadata. Page images are files referenced by <see cref="Page.ImagePath"/>; no
/// column holds image data (doc §3.7). Change the model only together with a new migration
/// (<c>dotnet ef migrations add &lt;Name&gt; --project src/IProfiler.Data</c>); a test fails otherwise.
/// </summary>
public sealed class CaptureDbContext(DbContextOptions<CaptureDbContext> options) : DbContext(options)
{
    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Page> Pages => Set<Page>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Batch>(b =>
        {
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200);
            b.HasMany(x => x.Documents).WithOne().HasForeignKey("BatchId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Document>(d =>
        {
            d.Property(x => x.Id).ValueGeneratedNever();
            d.HasIndex("BatchId", nameof(Document.Order));
            d.HasMany(x => x.Pages).WithOne().HasForeignKey("DocumentId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Page>(p =>
        {
            p.Property(x => x.Id).ValueGeneratedNever();
            p.Property(x => x.ImagePath).HasMaxLength(1024);
            p.HasIndex("DocumentId", nameof(Page.Order));
            // Stored as the angle in degrees (the enum's value), so the column reads 0/90/180/270.
            p.Property(x => x.Rotation).HasConversion<int>();
            p.Property(x => x.PatchCode).HasConversion<string>().HasMaxLength(16);
            p.ComplexProperty(x => x.Crop);
        });
    }
}

/// <summary>Used by <c>dotnet ef</c> at design time only; the app builds its options from <c>AppPaths.Database</c>.</summary>
internal sealed class DesignTimeCaptureDbContextFactory : IDesignTimeDbContextFactory<CaptureDbContext>
{
    public CaptureDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CaptureDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
