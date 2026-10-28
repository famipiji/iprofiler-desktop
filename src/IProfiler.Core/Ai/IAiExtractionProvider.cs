using IProfiler.Core.Recognition;

namespace IProfiler.Core.Ai;

/// <summary>
/// Optional AI field extraction (D7). Off by default; only used when a site has a reachable AI
/// server or a capable local GPU. The capture flow must work fully without it.
/// </summary>
public interface IAiExtractionProvider
{
    /// <summary>False when no AI backend is configured or reachable; callers must then skip extraction.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns a value for each field it could extract; fields it couldn't are absent.</summary>
    Task<IReadOnlyDictionary<string, string>> ExtractFieldsAsync(
        string imagePath,
        OcrResult ocr,
        IReadOnlyList<string> fieldNames,
        CancellationToken cancellationToken = default);
}
