using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;

namespace IProfiler.Core.Separation;

/// <summary>Typed settings for one <see cref="SeparationMode"/>; there is exactly one record per mode.</summary>
public abstract record SeparationSettings
{
    public abstract SeparationMode Mode { get; }
}

/// <summary>Start a new document every <see cref="PagesPerDocument"/> pages.</summary>
public sealed record PageCountSettings(int PagesPerDocument) : SeparationSettings
{
    public override SeparationMode Mode => SeparationMode.PageCount;
}

/// <summary>
/// A page carrying a matching barcode starts a new document.
/// A null <see cref="Symbology"/> or <see cref="TextPrefix"/> matches any barcode.
/// </summary>
public sealed record BarcodeSettings(
    BarcodeSymbology? Symbology = null,
    string? TextPrefix = null,
    bool RemoveSeparatorPage = true) : SeparationSettings
{
    public override SeparationMode Mode => SeparationMode.Barcode;
}

/// <summary>A page whose white-pixel ratio is at least <see cref="WhiteRatioThreshold"/> separates documents.</summary>
public sealed record BlankPageSettings(
    double WhiteRatioThreshold = 0.99,
    bool RemoveSeparatorPage = true) : SeparationSettings
{
    public override SeparationMode Mode => SeparationMode.BlankPage;
}

/// <summary>A sheet with the hardware patch code <see cref="SeparatorPatch"/> separates documents.</summary>
public sealed record PatchCodeSettings(
    PatchCode SeparatorPatch = PatchCode.PatchT,
    bool RemoveSeparatorPage = true) : SeparationSettings
{
    public override SeparationMode Mode => SeparationMode.PatchCode;
}

/// <summary>
/// A page whose OCR text inside <see cref="Zone"/> contains <see cref="Keyword"/> (case-insensitive)
/// starts a new document. The page itself is kept as the first page of that document.
/// </summary>
public sealed record KeywordZoneSettings(PixelRect Zone, string Keyword) : SeparationSettings
{
    public override SeparationMode Mode => SeparationMode.KeywordZone;
}
