using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;

namespace IProfiler.Core.Export;

public interface IPdfAssembler
{
    /// <summary>
    /// Writes one PDF with the pages in order, rotation and crop applied. Pages with
    /// <see cref="ExportPage.Ocr"/> get an invisible text layer at the word positions (PDF-OCR export).
    /// </summary>
    Task CreatePdfAsync(IReadOnlyList<ExportPage> pages, string outputPath, CancellationToken cancellationToken = default);
}

public sealed record ExportPage(Page Page, OcrResult? Ocr = null);
