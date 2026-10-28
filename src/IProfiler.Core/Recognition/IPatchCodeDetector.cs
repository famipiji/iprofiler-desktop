using IProfiler.Core.Domain;

namespace IProfiler.Core.Recognition;

/// <summary>
/// Software patch-code detection from the page image. It's the fallback for scanners that don't report patch
/// codes themselves through TWAIN (the scanner's own detection is preferred; see Task 1.1).
/// </summary>
public interface IPatchCodeDetector
{
    /// <returns>The patch code on the page, or null if there is none.</returns>
    Task<PatchCode?> DetectAsync(string imagePath, CancellationToken cancellationToken = default);
}
