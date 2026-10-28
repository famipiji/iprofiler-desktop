using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>
/// The canned 8-page batch every fake provider answers from, keyed by image file name.
/// It contains one separator of each kind so every separation mode has something to find:
/// keyword "INVOICE" on p01/p04/p07, a Code39 separator sheet on p03, a blank page on p05,
/// and a Patch T sheet on p06. Only used until the real providers arrive (Milestone 1).
/// </summary>
public static class FakeBatch
{
    public sealed record FakePage(
        string FileName,
        string ZoneText = "",
        BarcodeResult? Barcode = null,
        double WhiteRatio = 0.85,
        PatchCode? PatchCode = null);

    public static IReadOnlyList<FakePage> Pages { get; } =
    [
        new("p01.tif", ZoneText: "INVOICE 1001"),
        new("p02.tif"),
        new("p03.tif", Barcode: new BarcodeResult(BarcodeSymbology.Code39, "SEP-0001", new PixelRect(200, 300, 900, 150))),
        new("p04.tif", ZoneText: "INVOICE 1002"),
        new("p05.tif", WhiteRatio: 0.997),
        new("p06.tif", PatchCode: Core.Domain.PatchCode.PatchT),
        new("p07.tif", ZoneText: "INVOICE 1003"),
        new("p08.tif"),
    ];

    internal static FakePage? Find(string imagePath) =>
        Pages.FirstOrDefault(p => string.Equals(p.FileName, Path.GetFileName(imagePath), StringComparison.OrdinalIgnoreCase));
}
