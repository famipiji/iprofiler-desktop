namespace IProfiler.Core.Domain;

/// <summary>
/// One scanned or imported page. The image at <see cref="ImagePath"/> is never modified
/// (CLAUDE.md rule 7): rotation, crop and order are metadata applied on export.
/// </summary>
public sealed class Page
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Full path of the original page image (TIFF), stored untouched.</summary>
    public required string ImagePath { get; init; }

    /// <summary>
    /// Quarter-turn rotation applied on export, set by orientation detection (e.g. a sheet fed upside down)
    /// or by the user.
    /// </summary>
    public PageRotation Rotation { get; set; }

    /// <summary>
    /// Skew correction in degrees (positive = clockwise), measured on the original image and applied on
    /// export only; null = not measured.
    /// </summary>
    public double? DeskewAngle { get; set; }

    /// <summary>
    /// Resolution of the original image in dots per inch (from the scan request, or the image's resolution
    /// tag on import); null = not recorded, so export reads the image's own tag and estimates only as a last resort.
    /// </summary>
    public int? Dpi { get; set; }

    /// <summary>Crop rectangle in original-image pixels; null means the whole page.</summary>
    public PixelRect? Crop { get; set; }

    /// <summary>Position of the page within its document (0-based).</summary>
    public int Order { get; set; }

    /// <summary>Patch code reported by the scanner hardware for this sheet, if any (A6).</summary>
    public PatchCode? PatchCode { get; init; }
}
