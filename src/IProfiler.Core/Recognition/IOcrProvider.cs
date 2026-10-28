using IProfiler.Core.Domain;

namespace IProfiler.Core.Recognition;

/// <summary>Text recognition. Boxes are in original-image pixel coordinates.</summary>
public interface IOcrProvider
{
    Task<OcrResult> RecognizePageAsync(string imagePath, CancellationToken cancellationToken = default);

    Task<OcrResult> RecognizeRegionAsync(string imagePath, PixelRect region, CancellationToken cancellationToken = default);

    /// <summary>Whether the page reads upright or upside down (a sheet fed the wrong way round).</summary>
    Task<PageOrientation> DetectOrientationAsync(string imagePath, CancellationToken cancellationToken = default);
}

/// <param name="Rotation">The rotation that makes the page upright: <see cref="PageRotation.None"/> or <see cref="PageRotation.Rotate180"/>.</param>
/// <param name="UprightScore">Recognition confidence (0..1) reading the sampled lines as they are.</param>
/// <param name="RotatedScore">Recognition confidence (0..1) reading the same lines turned 180 degrees.</param>
/// <param name="LinesSampled">Text lines compared; 0 means no text was found and the page is left as it is.</param>
public sealed record PageOrientation(PageRotation Rotation, double UprightScore, double RotatedScore, int LinesSampled)
{
    public static PageOrientation Unknown { get; } = new(PageRotation.None, 0, 0, 0);
}

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines)
{
    public static OcrResult Empty { get; } = new([]);

    public string Text => string.Join(Environment.NewLine, Lines.Select(l => l.Text));

    /// <summary>
    /// The same text with every line and word box passed through <paramref name="map"/>, e.g.
    /// <c>ExportGeometry.ToOriginal</c> for OCR read from an export render.
    /// </summary>
    public OcrResult MapBoxes(Func<PixelRect, PixelRect> map) => new(Lines
        .Select(l => l with { Box = map(l.Box), Words = l.Words.Select(w => w with { Box = map(w.Box) }).ToList() })
        .ToList());
}

/// <param name="Words">The engine's own word boxes (used for searchable-PDF text layers and highlights).</param>
public sealed record OcrLine(string Text, PixelRect Box, double Confidence, IReadOnlyList<OcrWord> Words);

public sealed record OcrWord(string Text, PixelRect Box);
