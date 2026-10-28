using System.Security.Cryptography;
using System.Text.Json;
using IProfiler.Core.Domain;
using IProfiler.Core.Separation;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace IProfiler.Tests.Imaging;

/// <summary>Deskew, blank-page ratio and export rendering, mostly on 300 dpi 1-bit G4 pages (tools/make-imaging-samples.py).</summary>
public sealed class OpenCvImageProcessorTests : IDisposable
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "TestData");
    private static readonly double DefaultBlankThreshold = new BlankPageSettings().WhiteRatioThreshold;

    private readonly OpenCvImageProcessor _images = new(NullLogger<OpenCvImageProcessor>.Instance);
    private readonly string _out = Path.Combine(Path.GetTempPath(), "iProfiler-tests", Guid.NewGuid().ToString("N"));

    public static TheoryData<string, double> SkewedPages()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "imaging", "imaging.json")));
        var data = new TheoryData<string, double>();
        foreach (var p in json.RootElement.GetProperty("skewCorrection").EnumerateObject())
            data.Add(p.Name, p.Value.GetDouble());
        return data;
    }

    public static TheoryData<string> BlankPages => new(
        "imaging/blank-g4.tif", "imaging/near-blank-staple-g4.tif", "imaging/near-blank-staple-gray.jpg");

    public static TheoryData<string> ContentPages => new(
        "barcode/bw-g4-300dpi.tif", "imaging/skew-3deg-g4.tif", "ocr/invoice-150dpi.png", "patch/patch-T.tif");

    [Theory]
    [MemberData(nameof(SkewedPages))]
    public async Task Skew_correction_matches_the_generated_skew(string page, double expected)
    {
        var angle = await _images.DetectSkewAngleAsync(Path.Combine(Data, page));

        Assert.InRange(angle, expected - 0.15, expected + 0.15);
    }

    [Fact]
    public async Task Page_without_text_has_no_skew()
    {
        Assert.Equal(0, await _images.DetectSkewAngleAsync(Path.Combine(Data, "imaging", "near-blank-staple-g4.tif")));
    }

    [Theory]
    [MemberData(nameof(BlankPages))]
    public async Task Blank_and_stapled_blank_pages_are_blank_at_the_default_threshold(string page)
    {
        Assert.True(await _images.GetWhiteRatioAsync(Path.Combine(Data, page)) >= DefaultBlankThreshold);
    }

    [Theory]
    [MemberData(nameof(ContentPages))]
    public async Task Pages_with_content_are_not_blank_at_the_default_threshold(string page)
    {
        Assert.True(await _images.GetWhiteRatioAsync(Path.Combine(Data, page)) < DefaultBlankThreshold);
    }

    [Fact]
    public async Task Export_applies_rotation_but_never_touches_the_original()
    {
        var original = Path.Combine(Data, "imaging", "upside-down-g4.tif");
        var before = Hash(original);
        var page = new Page { ImagePath = original, Rotation = PageRotation.Rotate180 };
        var output = Path.Combine(_out, "upright.png");

        await _images.RenderForExportAsync(page, output);

        Assert.Equal(before, Hash(original));
        // upside-down-g4 is the delivery note turned 180 degrees, so turning it back is pixel-exact.
        using var exported = Cv2.ImRead(output, ImreadModes.Grayscale);
        using var note = Cv2.ImDecode(File.ReadAllBytes(Path.Combine(Data, "barcode", "bw-g4-300dpi.tif")), ImreadModes.Grayscale);
        using var diff = new Mat();
        Cv2.Absdiff(exported, note, diff);
        Assert.Equal(0, Cv2.CountNonZero(diff));
    }

    [Fact]
    public async Task Export_crops_deskews_and_turns_a_quarter_and_keeps_bilevel_pages_bilevel()
    {
        var original = Path.Combine(Data, "imaging", "skew-3deg-g4.tif");
        var before = Hash(original);
        var page = new Page
        {
            ImagePath = original,
            Crop = new PixelRect(100, 200, 2000, 3000),
            DeskewAngle = 3.0,
            Rotation = PageRotation.Clockwise90,
        };
        var output = Path.Combine(_out, "rotated.png");

        await _images.RenderForExportAsync(page, output);

        Assert.Equal(before, Hash(original));
        using var exported = Cv2.ImRead(output, ImreadModes.Unchanged);
        Assert.Equal((3000, 2000), (exported.Width, exported.Height));
        using var midTones = new Mat();
        Cv2.InRange(exported, new Scalar(1), new Scalar(254), midTones);
        Assert.Equal(0, Cv2.CountNonZero(midTones));
        // The exported page is straight now.
        Assert.InRange(await _images.DetectSkewAngleAsync(output), -0.15, 0.15);
    }

    [Fact]
    public async Task Export_refuses_to_overwrite_the_original()
    {
        var original = Path.Combine(Data, "imaging", "blank-g4.tif");
        var page = new Page { ImagePath = original, Rotation = PageRotation.Rotate180 };

        await Assert.ThrowsAsync<ArgumentException>(() => _images.RenderForExportAsync(page, original));
    }

    public void Dispose()
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, recursive: true);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
