using IProfiler.Core.Domain;

namespace IProfiler.Core.Recognition;

/// <summary>Text recognition. Boxes are in original-image pixel coordinates.</summary>
public interface IOcrProvider
{
    Task<OcrResult> RecognizePageAsync(string imagePath, CancellationToken cancellationToken = default);

    Task<OcrResult> RecognizeRegionAsync(string imagePath, PixelRect region, CancellationToken cancellationToken = default);
}

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines)
{
    public static OcrResult Empty { get; } = new([]);

    public string Text => string.Join(Environment.NewLine, Lines.Select(l => l.Text));
}

/// <param name="Words">The engine's own word boxes (used for searchable-PDF text layers and highlights).</param>
public sealed record OcrLine(string Text, PixelRect Box, double Confidence, IReadOnlyList<OcrWord> Words);

public sealed record OcrWord(string Text, PixelRect Box);
