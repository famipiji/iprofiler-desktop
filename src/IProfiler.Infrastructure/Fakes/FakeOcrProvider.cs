using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Returns each page's canned zone text, whatever region is asked for.</summary>
public sealed class FakeOcrProvider : IOcrProvider
{
    private static readonly PixelRect ZoneBox = new(100, 100, 1200, 120);

    public Task<OcrResult> RecognizePageAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Recognize(imagePath));

    public Task<OcrResult> RecognizeRegionAsync(string imagePath, PixelRect region, CancellationToken cancellationToken = default) =>
        Task.FromResult(Recognize(imagePath));

    private static OcrResult Recognize(string imagePath)
    {
        var text = FakeBatch.Find(imagePath)?.ZoneText;
        if (string.IsNullOrEmpty(text))
            return OcrResult.Empty;

        return new OcrResult([new OcrLine(text, ZoneBox, 0.98, [new OcrWord(text, ZoneBox)])]);
    }
}
