using IProfiler.Core;
using IProfiler.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IProfiler.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>The SQLite database at <see cref="AppPaths.Database"/> and the repositories on top of it.</summary>
    public static IServiceCollection AddCaptureDatabase(this IServiceCollection services, AppPaths paths)
    {
        services.AddDbContextFactory<CaptureDbContext>(o => o.UseSqlite($"Data Source={paths.Database}"));
        services.AddSingleton<IBatchRepository, EfBatchRepository>();
        return services;
    }

    /// <summary>Creates the database or brings it up to the current schema. Call once at startup.</summary>
    public static async Task MigrateCaptureDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var paths = services.GetRequiredService<AppPaths>();
        Directory.CreateDirectory(paths.Data);
        await using var db = await services.GetRequiredService<IDbContextFactory<CaptureDbContext>>().CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
