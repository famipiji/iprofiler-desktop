using System.Security.Cryptography;
using System.Text;
using BitMiracle.LibTiff.Classic;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Export;
using IProfiler.Core.Recognition;
using IProfiler.Infrastructure.Export;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace IProfiler.Tests.Export;

/// <summary>
/// PDF / PDF-OCR and multi-page TIFF export. Phrase search and highlight positions in real readers are
/// checked by tools/check-searchable-pdf.py on the spike output (see docs/spike-report.md).
/// </summary>
public sealed class AssemblerTests : IDisposable
{
    private const double A4WidthPt = 595.3;
    private const double A4HeightPt = 841.9;

    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "TestData");
    private static readonly string NoteG4 = Path.Combine(Data, "barcode", "bw-g4-300dpi.tif"); // 300 dpi tag
    private static readonly string InvoicePng = Path.Combine(Data, "ocr", "invoice-150dpi.png"); // no resolution tag
    private static readonly string StapleJpeg = Path.Combine(Data, "imaging", "near-blank-staple-gray.jpg");

    private readonly AppPaths _paths = new(
        AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "iProfiler-tests", Guid.NewGuid().ToString("N")));

    private readonly OpenCvImageProcessor _images = new(NullLogger<OpenCvImageProcessor>.Instance);

    private string Out(string name) => Path.Combine(_paths.Data, "out", name);

    [Fact]
    public async Task Pdf_pages_take_their_size_from_the_resolution_and_rotation()
    {
        var pdf = new PdfSharpAssembler(_images, _paths, NullLogger<PdfSharpAssembler>.Instance);
        var output = Out("sizes.pdf");

        await pdf.CreatePdfAsync(
        [
            new ExportPage(new Page { ImagePath = NoteG4 }), // TIFF tag: 300 dpi
            new ExportPage(new Page { ImagePath = NoteG4, Rotation = PageRotation.Clockwise90 }),
            new ExportPage(new Page { ImagePath = NoteG4, Dpi = 200 }), // recorded by the scanner: wins over the tag
            new ExportPage(new Page { ImagePath = InvoicePng }), // untagged: estimated from the A4 width
            new ExportPage(new Page { ImagePath = NoteG4, Crop = new PixelRect(0, 0, 1240, 1754) }),
        ], output);

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var sizes = document.Pages.Cast<PdfPage>().Select(p => (W: p.Width.Point, H: p.Height.Point)).ToList();
        Assert.Equal(5, sizes.Count);
        AssertSize((A4WidthPt, A4HeightPt), sizes[0]);
        AssertSize((A4HeightPt, A4WidthPt), sizes[1]);
        AssertSize((2480 * 72 / 200.0, 3508 * 72 / 200.0), sizes[2]);
        AssertSize((A4WidthPt, A4HeightPt), sizes[3]);
        AssertSize((A4WidthPt / 2, A4HeightPt / 2), sizes[4]);
        Assert.Empty(Directory.GetFileSystemEntries(_paths.Temp)); // rendered pages are cleaned up
    }

    [Fact]
    public async Task Pdf_ocr_text_layer_is_invisible_and_only_on_pages_with_ocr()
    {
        var pdf = new PdfSharpAssembler(_images, _paths, NullLogger<PdfSharpAssembler>.Instance);
        var output = Out("ocr.pdf");
        var before = Hash(NoteG4);
        var ocr = new OcrResult(
        [
            new OcrLine("SIMEN UTARA", new PixelRect(300, 400, 900, 80), 0.97,
                [new OcrWord("SIMEN", new PixelRect(300, 400, 420, 80)), new OcrWord("UTARA", new PixelRect(800, 400, 400, 80))]),
            new OcrLine("Jumlah RM 1,234.50", new PixelRect(300, 600, 1000, 50), 0.95, []),
        ]);

        await pdf.CreatePdfAsync(
        [
            new ExportPage(new Page { ImagePath = NoteG4, DeskewAngle = 0.5, Crop = new PixelRect(50, 50, 2380, 3400) }, ocr),
            new ExportPage(new Page { ImagePath = NoteG4 }),
        ], output);

        Assert.Equal(before, Hash(NoteG4));
        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var withOcr = Content(document.Pages[0]);
        Assert.Contains("3 Tr", withOcr);
        // Two words, the stretched space between them, and the line without word boxes: four strings.
        Assert.Equal(4, CountOperator(withOcr, "Tj"));
        Assert.Equal(CountOperator(withOcr, "BT"), CountOperator(withOcr, "3 Tr"));
        Assert.DoesNotContain("Tj", Content(document.Pages[1]));
    }

    [Fact]
    public async Task Multi_page_tiff_keeps_the_order_resolution_and_bilevel_compression()
    {
        var tiff = new LibTiffAssembler(_images, _paths, NullLogger<LibTiffAssembler>.Instance);
        var output = Out("batch.tif");
        var before = Hash(NoteG4);

        await tiff.CreateMultiPageTiffAsync(
        [
            new Page { ImagePath = NoteG4 },
            new Page { ImagePath = NoteG4, Rotation = PageRotation.Clockwise90, DeskewAngle = 1.0 },
            new Page { ImagePath = StapleJpeg, Crop = new PixelRect(0, 0, 1000, 500) },
        ], output);

        Assert.Equal(before, Hash(NoteG4));
        using var file = Tiff.Open(output, "r");
        var pages = new List<string>();
        do
        {
            pages.Add($"{file.GetField(TiffTag.IMAGEWIDTH)[0].ToInt()}x{file.GetField(TiffTag.IMAGELENGTH)[0].ToInt()} " +
                      $"{file.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt()}bit {(Compression)file.GetField(TiffTag.COMPRESSION)[0].ToInt()} " +
                      $"{file.GetField(TiffTag.XRESOLUTION)[0].ToDouble():F0}dpi page {file.GetField(TiffTag.PAGENUMBER)[0].ToInt()}");
        }
        while (file.ReadDirectory());

        Assert.Equal(
        [
            "2480x3508 1bit CCITTFAX4 300dpi page 0",
            "3508x2480 1bit CCITTFAX4 300dpi page 1",
            "1000x500 8bit LZW 300dpi page 2",
        ], pages);
    }

    [Fact]
    public async Task Tiff_page_is_pixel_identical_to_the_original_when_nothing_is_applied()
    {
        var tiff = new LibTiffAssembler(_images, _paths, NullLogger<LibTiffAssembler>.Instance);
        var output = Out("single.tif");

        await tiff.CreateMultiPageTiffAsync([new Page { ImagePath = NoteG4 }], output);

        using var original = Cv2.ImRead(NoteG4, ImreadModes.Grayscale);
        using var exported = Cv2.ImRead(output, ImreadModes.Grayscale);
        using var diff = new Mat();
        Cv2.Absdiff(original, exported, diff);
        Assert.Equal(0, Cv2.CountNonZero(diff));
    }

    public void Dispose()
    {
        if (Directory.Exists(_paths.Data))
            Directory.Delete(_paths.Data, recursive: true);
    }

    private static void AssertSize((double W, double H) expected, (double W, double H) actual)
    {
        Assert.InRange(actual.W, expected.W - 0.5, expected.W + 0.5);
        Assert.InRange(actual.H, expected.H - 0.5, expected.H + 0.5);
    }

    private static string Content(PdfPage page) =>
        string.Join("\n", Enumerable.Range(0, page.Contents.Elements.Count)
            .Select(i => Encoding.Latin1.GetString(page.Contents.Elements.GetDictionary(i)!.Stream!.UnfilteredValue)));

    private static int CountOperator(string content, string op) =>
        content.Split('\n').Count(l => l == op || l.EndsWith(" " + op, StringComparison.Ordinal));

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
