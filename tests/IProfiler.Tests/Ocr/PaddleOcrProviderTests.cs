using System.Security.Cryptography;
using System.Text.Json;
using IProfiler.Application.Pages;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;
using IProfiler.Infrastructure.Imaging;
using IProfiler.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;

namespace IProfiler.Tests.Ocr;

/// <summary>One engine for the whole class: loading the models takes a couple of seconds.</summary>
public sealed class PaddleOcrFixture : IDisposable
{
    public PaddleOcrFixture()
    {
        var paths = new AppPaths(AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "iProfiler-tests"));
        Provider = new PaddleOcrProvider(paths, NullLogger<PaddleOcrProvider>.Instance);
        Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ocr", "invoice-150dpi.json")));
    }

    public PaddleOcrProvider Provider { get; }

    public JsonDocument Fixture { get; }

    public static string Invoice => Path.Combine(AppContext.BaseDirectory, "TestData", "ocr", "invoice-150dpi.png");

    public string FieldText => Fixture.RootElement.GetProperty("fieldText").GetString()!;

    public PixelRect FieldRegion
    {
        get
        {
            var r = Fixture.RootElement.GetProperty("fieldRegion").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            return new PixelRect(r[0], r[1], r[2], r[3]);
        }
    }

    public void Dispose()
    {
        Provider.Dispose();
        Fixture.Dispose();
    }
}

public class PaddleOcrProviderTests(PaddleOcrFixture fixture) : IClassFixture<PaddleOcrFixture>
{
    private const int PageWidth = 1240;
    private const int PageHeight = 1754;

    [Fact]
    public void Models_ship_in_the_output_folder()
    {
        // Offline (rule 8): the models are files next to the app, never fetched at runtime.
        Assert.True(File.Exists(Path.Combine(fixture.Provider.DetectionModelDirectory, "inference.pdiparams")));
        Assert.True(File.Exists(Path.Combine(fixture.Provider.RecognitionModelDirectory, "inference.pdiparams")));
    }

    [Fact]
    public async Task Page_ocr_reads_the_invoice()
    {
        var result = await fixture.Provider.RecognizePageAsync(PaddleOcrFixture.Invoice);

        Assert.Contains("MERIDIAN OFFICE SUPPLIES", result.Text);
        Assert.Contains(fixture.FieldText, result.Text);
        Assert.Contains("TOTAL: RM 1,234.50", result.Text);
        Assert.All(result.Lines, l => Assert.True(l.Confidence >= PaddleOcrProvider.MinLineConfidence));
        Assert.All(result.Lines, l => AssertInside(l.Box, new PixelRect(0, 0, PageWidth, PageHeight)));
    }

    [Fact]
    public async Task Page_ocr_reads_a_bilevel_g4_tiff_at_300_dpi()
    {
        // Default scanner output: 1-bit CCITT G4. Thin B/W strokes must survive the 1600 px downscale.
        var page = Path.Combine(AppContext.BaseDirectory, "TestData", "barcode", "bw-g4-300dpi.tif");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(page, ".json")));
        var words = json.RootElement.GetProperty("words").EnumerateArray().Select(w => w.GetString()!).ToList();

        var result = await fixture.Provider.RecognizePageAsync(page);

        Assert.All(words, w => Assert.Contains(w, result.Text));
        Assert.Contains("Ship to: Meridian Office Supplies, Petaling Jaya", result.Text);
    }

    [Theory]
    [InlineData("imaging/upside-down-g4.tif", PageRotation.Rotate180)]
    [InlineData("barcode/bw-g4-300dpi.tif", PageRotation.None)]
    [InlineData("imaging/skew-3deg-g4.tif", PageRotation.None)]
    [InlineData("ocr/invoice-150dpi.png", PageRotation.None)]
    public async Task Orientation_detects_pages_fed_upside_down(string page, PageRotation expected)
    {
        var orientation = await fixture.Provider.DetectOrientationAsync(Path.Combine(AppContext.BaseDirectory, "TestData", page));

        Assert.Equal(expected, orientation.Rotation);
        Assert.True(Math.Abs(orientation.UprightScore - orientation.RotatedScore) > 0.3,
            $"scores too close to call: {orientation}");
    }

    [Fact]
    public async Task Orientation_of_a_blank_page_is_left_alone()
    {
        var orientation = await fixture.Provider.DetectOrientationAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "imaging", "blank-g4.tif"));

        Assert.Equal(PageOrientation.Unknown, orientation);
    }

    [Fact]
    public async Task Page_analyzer_stores_rotation_and_deskew_as_metadata_only()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "imaging", "upside-down-g4.tif");
        var before = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var analyzer = new PageAnalyzer(fixture.Provider, new OpenCvImageProcessor(NullLogger<OpenCvImageProcessor>.Instance),
            NullLogger<PageAnalyzer>.Instance);
        var page = new Page { ImagePath = path };

        await analyzer.AnalyzeAsync(page);

        Assert.Equal(PageRotation.Rotate180, page.Rotation);
        Assert.InRange(page.DeskewAngle!.Value, 0.35, 0.65);
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
    }

    [Fact]
    public async Task Page_ocr_keeps_reading_order_left_to_right()
    {
        var result = await fixture.Provider.RecognizePageAsync(PaddleOcrFixture.Invoice);
        var text = result.Text;

        Assert.True(text.IndexOf("Invoice No", StringComparison.Ordinal) < text.IndexOf(fixture.FieldText, StringComparison.Ordinal));
        Assert.True(text.IndexOf("Description", StringComparison.Ordinal) < text.IndexOf("Amount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Word_boxes_come_from_the_line_pixels()
    {
        var result = await fixture.Provider.RecognizePageAsync(PaddleOcrFixture.Invoice);
        var line = result.Lines.Single(l => l.Text.StartsWith("Toner Cartridge", StringComparison.Ordinal));

        Assert.Equal(["Toner", "Cartridge", "TN-2480"], line.Words.Select(w => w.Text));
        Assert.All(line.Words, w => AssertInside(w.Box, line.Box));
        // Left to right, not overlapping.
        Assert.True(line.Words[0].Box.X + line.Words[0].Box.Width <= line.Words[1].Box.X);
        Assert.True(line.Words[1].Box.X + line.Words[1].Box.Width <= line.Words[2].Box.X);
    }

    [Fact]
    public async Task Region_ocr_reads_one_field_and_returns_page_coordinates()
    {
        var region = fixture.FieldRegion;

        var result = await fixture.Provider.RecognizeRegionAsync(PaddleOcrFixture.Invoice, region);

        var line = Assert.Single(result.Lines);
        Assert.Equal(fixture.FieldText, line.Text);
        AssertInside(line.Box, region);
        Assert.True(line.Box.Width > region.Width / 2, $"box {line.Box} should cover most of region {region}");
    }

    [Fact]
    public async Task Region_outside_the_page_is_empty()
    {
        var result = await fixture.Provider.RecognizeRegionAsync(PaddleOcrFixture.Invoice, new PixelRect(5000, 5000, 100, 100));

        Assert.Same(OcrResult.Empty, result);
    }

    [Fact]
    public async Task Region_partly_outside_the_page_is_clipped()
    {
        var result = await fixture.Provider.RecognizeRegionAsync(PaddleOcrFixture.Invoice, new PixelRect(-50, -50, 2000, 200));

        Assert.Contains("MERIDIAN", result.Text);
        Assert.All(result.Lines, l => AssertInside(l.Box, new PixelRect(0, 0, PageWidth, 150)));
    }

    [Fact]
    public async Task Concurrent_calls_are_serialised_and_all_succeed()
    {
        var region = fixture.FieldRegion;
        var calls = Enumerable.Range(0, 4)
            .Select(_ => fixture.Provider.RecognizeRegionAsync(PaddleOcrFixture.Invoice, region));

        var results = await Task.WhenAll(calls);

        Assert.All(results, r => Assert.Equal(fixture.FieldText, r.Text));
    }

    private static void AssertInside(PixelRect inner, PixelRect outer) =>
        Assert.True(inner.X >= outer.X && inner.Y >= outer.Y &&
                    inner.X + inner.Width <= outer.X + outer.Width &&
                    inner.Y + inner.Height <= outer.Y + outer.Height,
            $"{inner} is not inside {outer}");
}
