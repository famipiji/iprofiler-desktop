using IProfiler.Core.Domain;

namespace IProfiler.Core.Imaging;

/// <summary>
/// Runs once per new page (scan, import, hot folder): measures orientation and skew and stores them as page
/// metadata (<see cref="Page.Rotation"/>, <see cref="Page.DeskewAngle"/>). The image itself is never changed.
/// </summary>
public interface IPageAnalyzer
{
    Task AnalyzeAsync(Page page, CancellationToken cancellationToken = default);
}
