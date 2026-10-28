using System.Text.Json;
using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IProfiler.Tests.Ocr;

/// <summary>
/// Word boxes from ink gaps, against the true word positions of tools/make-pdf-samples.py: letter-spaced
/// headings, very wide spacing ("TERIMA KASIH"), big and small text on one line. These boxes become the
/// searchable PDF's text layer, so each word has to be a word of its own, in the right place.
/// </summary>
[Collection(PaddleOcrCollection.Name)]
public sealed class WordBoxTests(PaddleOcrFixture fixture) : IDisposable
{
    // Ends of a word box may be this far off (300 dpi pixels, ~0.5 mm), or overlap the true box by 80%.
    private const int EdgeTolerance = 6;

    private static readonly string Pdf = Path.Combine(AppContext.BaseDirectory, "TestData", "pdf");
    private readonly string _out = Path.Combine(Path.GetTempPath(), "iProfiler-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Every_word_of_the_upright_page_has_its_own_box()
    {
        var result = await fixture.Provider.RecognizePageAsync(Path.Combine(Pdf, "phrases-g4.tif"));

        AssertWords(result, "phrases-g4.tif");
    }

    [Fact]
    public async Task Every_word_of_the_straightened_skewed_page_has_its_own_box()
    {
        // OCR of the deskewed export render, as the PDF-OCR export does; the generator skewed about the centre,
        // so straightening about the centre puts the words back on their true positions.
        var images = new OpenCvImageProcessor(NullLogger<OpenCvImageProcessor>.Instance);
        var page = new Page { ImagePath = Path.Combine(Pdf, "phrases-skew-g4.tif"), DeskewAngle = 1.5 };
        var straight = Path.Combine(_out, "straight.png");
        await images.RenderForExportAsync(page, straight);

        var result = await fixture.Provider.RecognizePageAsync(straight);

        AssertWords(result, "phrases-skew-g4.tif");
    }

    public void Dispose()
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, recursive: true);
    }

    private static void AssertWords(OcrResult result, string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pdf, "phrases.json")));
        var truth = json.RootElement.GetProperty("pages").GetProperty(file).GetProperty("words").EnumerateArray()
            .Select(w => (Text: w.GetProperty("text").GetString()!, Box: Box(w.GetProperty("box"))))
            .ToList();
        var words = result.Lines.SelectMany(l => l.Words).ToList();

        var misplaced = truth
            .Where(t => !words.Any(w => w.Text == t.Text && SameSpot(w.Box, t.Box)))
            .Select(t => $"{t.Text} {t.Box} (OCR: {string.Join(", ", words.Where(w => w.Text == t.Text).Select(w => w.Box))})")
            .ToList();

        Assert.True(misplaced.Count == 0, $"{misplaced.Count}/{truth.Count} words missing or misplaced:\n" + string.Join("\n", misplaced) +
                                          "\nOCR text:\n" + result.Text);
        Assert.Contains(result.Lines, l => l.Text.Contains("SIMEN UTARA SDN BHD", StringComparison.Ordinal));
    }

    private static bool SameSpot(PixelRect ocr, PixelRect truth)
    {
        var overlap = Math.Min(ocr.X + ocr.Width, truth.X + truth.Width) - Math.Max(ocr.X, truth.X);
        var span = Math.Max(ocr.X + ocr.Width, truth.X + truth.Width) - Math.Min(ocr.X, truth.X);
        var edges = Math.Abs(ocr.X - truth.X) <= EdgeTolerance && Math.Abs(ocr.X + ocr.Width - (truth.X + truth.Width)) <= EdgeTolerance;
        var centreY = truth.Y + truth.Height / 2;
        return (overlap >= 0.8 * span || edges) && ocr.Y <= centreY && centreY <= ocr.Y + ocr.Height;
    }

    private static PixelRect Box(JsonElement b)
    {
        var v = b.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        return new PixelRect(v[0], v[1], v[2], v[3]);
    }
}
