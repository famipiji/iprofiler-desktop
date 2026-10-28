using IProfiler.Core.Domain;

namespace IProfiler.Core.Recognition;

/// <summary>Barcode and QR reading. Boxes are in original-image pixel coordinates.</summary>
public interface IBarcodeProvider
{
    Task<IReadOnlyList<BarcodeResult>> ReadPageAsync(string imagePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BarcodeResult>> ReadRegionAsync(string imagePath, PixelRect region, CancellationToken cancellationToken = default);
}

public enum BarcodeSymbology
{
    Code39,
    Code128,
    QrCode,
    DataMatrix,
    Ean13,
    Pdf417,
    Other,
}

public sealed record BarcodeResult(BarcodeSymbology Symbology, string Text, PixelRect Box);
