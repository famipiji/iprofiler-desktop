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

    public PageRotation Rotation { get; set; }

    /// <summary>Crop rectangle in original-image pixels; null means the whole page.</summary>
    public PixelRect? Crop { get; set; }

    /// <summary>Position of the page within its document (0-based).</summary>
    public int Order { get; set; }

    /// <summary>Patch code reported by the scanner hardware for this sheet, if any (A6).</summary>
    public PatchCode? PatchCode { get; init; }
}
