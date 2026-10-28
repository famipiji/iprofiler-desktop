using IProfiler.Core.Ai;
using IProfiler.Core.Recognition;

namespace IProfiler.Infrastructure.Ai;

/// <summary>The default (D7): AI extraction is off, so it is never available and extracts nothing.</summary>
public sealed class NullAiExtractionProvider : IAiExtractionProvider
{
    public bool IsAvailable => false;

    public Task<IReadOnlyDictionary<string, string>> ExtractFieldsAsync(
        string imagePath,
        OcrResult ocr,
        IReadOnlyList<string> fieldNames,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}
