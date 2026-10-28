using System.Diagnostics;
using System.Text;
using IProfiler.Core;
using IProfiler.Core.Export;
using IProfiler.Core.Imaging;
using IProfiler.Core.Recognition;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace IProfiler.Infrastructure.Export;

/// <summary>
/// PDF and PDF-OCR export with PDFsharp (MIT). Each page is rendered through
/// <see cref="IImageProcessor.RenderForExportAsync"/> (crop, deskew, rotation) and placed at its real
/// resolution, so a 300 dpi A4 scan becomes an A4 page. Pages with OCR get an invisible text layer
/// (PDF text render mode 3): every word is stretched to its box, and the gap to the next word is filled
/// with a stretched space, so readers find phrases and highlight the right spot.
/// </summary>
/// <remarks>
/// OCR boxes are in original-image pixels and are mapped with the <see cref="ExportGeometry"/> the renderer
/// returns. The text is drawn left to right in the exported (upright) page, which is what
/// <see cref="Core.Domain.Page.Rotation"/> means: the turn that makes the page read upright.
/// </remarks>
public sealed class PdfSharpAssembler(IImageProcessor images, AppPaths paths, ILogger<PdfSharpAssembler> logger) : IPdfAssembler
{
    /// <summary>Font of the hidden text. Only its metrics matter (glyphs are never painted); subset-embedded.</summary>
    public const string TextLayerFont = "Arial";

    private const double PointsPerInch = 72;

    // Every word is drawn at this size and scaled to its box, so one font object serves the whole document.
    private const double BaseFontSize = 10;

    static PdfSharpAssembler()
    {
        // PDFsharp's core build has no font source of its own; Arial is on every Windows install.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    public async Task CreatePdfAsync(IReadOnlyList<ExportPage> pages, string outputPath, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var work = new ExportWorkFolder(paths, "pdf", logger);
        using var document = new PdfDocument();
        document.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
        document.Info.Creator = "iProfiler Desktop";
        var font = new XFont(TextLayerFont, BaseFontSize, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        var words = 0;

        for (var i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (page, ocr) = (pages[i].Page, pages[i].Ocr);
            // PNG: lossless, and PDFsharp reads it without any other imaging library.
            var rendered = work.PageFile(i, ".png");
            var geometry = await images.RenderForExportAsync(page, rendered, cancellationToken);
            var dpi = ImageFile.Dpi(page.ImagePath, geometry.OriginalWidth, geometry.OriginalHeight, page.Dpi);
            var scale = PointsPerInch / dpi;

            var pdfPage = document.AddPage();
            pdfPage.Width = XUnit.FromPoint(geometry.Width * scale);
            pdfPage.Height = XUnit.FromPoint(geometry.Height * scale);

            using (var image = XImage.FromStream(new MemoryStream(await File.ReadAllBytesAsync(rendered, cancellationToken))))
            using (var gfx = XGraphics.FromPdfPage(pdfPage))
                gfx.DrawImage(image, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);

            if (ocr is { Lines.Count: > 0 })
                words += AddTextLayer(pdfPage, ocr, geometry, scale, font);

            logger.LogInformation("PDF page {Number}: {File} {Width}x{Height} px at {Dpi:F0} dpi → {WidthPt:F1}x{HeightPt:F1} pt, {Lines} OCR lines",
                i + 1, Path.GetFileName(page.ImagePath), geometry.Width, geometry.Height, dpi,
                pdfPage.Width.Point, pdfPage.Height.Point, ocr?.Lines.Count ?? 0);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        document.Save(outputPath);
        logger.LogInformation("PDF {Output}: {Pages} pages, {Words} hidden words, {KB} KB in {ElapsedMs} ms",
            outputPath, pages.Count, words, new FileInfo(outputPath).Length / 1024, sw.ElapsedMilliseconds);
    }

    /// <summary>Draws the page's words into a content stream of their own, then makes that stream's text invisible.</summary>
    private static int AddTextLayer(PdfPage pdfPage, OcrResult ocr, ExportGeometry geometry, double scale, XFont font)
    {
        var count = 0;
        using (var gfx = XGraphics.FromPdfPage(pdfPage, XGraphicsPdfPageOptions.Append))
        {
            foreach (var line in ocr.Lines)
            {
                var words = line.Words.Count > 0 ? line.Words : [new OcrWord(line.Text, line.Box)];
                var boxes = words.Select(w => geometry.ToExport(w.Box)).ToList();
                for (var i = 0; i < words.Count; i++)
                {
                    var b = boxes[i];
                    DrawStretched(gfx, font, words[i].Text, b.X * scale, b.Y * scale, b.Width * scale, b.Height * scale);
                    count++;
                    if (i + 1 == words.Count)
                        continue;

                    // The space: from this word's right edge to the next word's left edge, as tall as this word.
                    var gap = Math.Max(boxes[i + 1].X - (b.X + b.Width), b.Height / 4.0);
                    DrawStretched(gfx, font, " ", (b.X + b.Width) * scale, b.Y * scale, gap * scale, b.Height * scale);
                }
            }
        }

        MakeTextInvisible(pdfPage.Contents.Elements.GetDictionary(pdfPage.Contents.Elements.Count - 1)!);
        return count;
    }

    /// <summary>Text scaled so its glyph cell (ascent to descent) fills the box exactly.</summary>
    private static void DrawStretched(XGraphics gfx, XFont font, string text, double x, double y, double width, double height)
    {
        var measured = gfx.MeasureString(text, font).Width;
        if (measured <= 0 || width <= 0 || height <= 0)
            return;

        var metrics = font.Metrics;
        var ascent = font.Size * metrics.Ascent / metrics.UnitsPerEm;
        var descent = font.Size * metrics.Descent / metrics.UnitsPerEm;
        var sx = width / measured;
        var sy = height / (ascent + descent);

        var state = gfx.Save();
        gfx.TranslateTransform(x, y + ascent * sy);
        gfx.ScaleTransform(sx, sy);
        gfx.DrawString(text, font, XBrushes.Black, 0, 0, XStringFormats.BaseLineLeft);
        gfx.Restore(state);
    }

    /// <summary>
    /// PDFsharp has no text-render-mode setting, so "3 Tr" (neither fill nor stroke) is added after every
    /// BT of the text layer's own stream. Only that stream is touched; the strings in it are hex-encoded,
    /// so "BT" can only be the operator.
    /// </summary>
    private static void MakeTextInvisible(PdfDictionary content)
    {
        var stream = content.Stream ?? throw new InvalidOperationException("Text layer has no content stream.");
        var text = Encoding.Latin1.GetString(stream.UnfilteredValue);
        var patched = text.Replace("BT\n", "BT\n3 Tr\n", StringComparison.Ordinal);
        if (patched == text)
            throw new InvalidOperationException("Text layer has no text object to make invisible.");

        stream.Value = Encoding.Latin1.GetBytes(patched);
        content.Elements.Remove(PdfDictionary.PdfStream.Keys.Filter);
        content.Elements.Remove(PdfDictionary.PdfStream.Keys.DecodeParms);
    }
}
