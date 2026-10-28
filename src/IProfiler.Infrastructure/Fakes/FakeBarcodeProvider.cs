using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Returns each page's canned barcode, whatever region is asked for.</summary>
public sealed class FakeBarcodeProvider : IBarcodeProvider
{
    public Task<IReadOnlyList<BarcodeResult>> ReadPageAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(imagePath));

    public Task<IReadOnlyList<BarcodeResult>> ReadRegionAsync(string imagePath, PixelRect region, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(imagePath));

    private static IReadOnlyList<BarcodeResult> Read(string imagePath) =>
        FakeBatch.Find(imagePath)?.Barcode is { } barcode ? [barcode] : [];
}
