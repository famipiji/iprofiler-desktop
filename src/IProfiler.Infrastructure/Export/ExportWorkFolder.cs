using IProfiler.Core;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Export;

/// <summary>A folder under <see cref="AppPaths.Temp"/> for one export's rendered pages, deleted afterwards.</summary>
internal sealed class ExportWorkFolder : IDisposable
{
    private readonly ILogger _logger;

    public ExportWorkFolder(AppPaths paths, string kind, ILogger logger)
    {
        _logger = logger;
        Path = System.IO.Path.Combine(paths.Temp, $"{kind}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Path for the rendered copy of page <paramref name="index"/> (0-based).</summary>
    public string PageFile(int index, string extension) => System.IO.Path.Combine(Path, $"page-{index + 1:D4}{extension}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete export working folder {Folder}", Path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not delete export working folder {Folder}", Path);
        }
    }
}
