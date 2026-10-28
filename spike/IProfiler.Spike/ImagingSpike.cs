using System.Diagnostics;
using System.Security.Cryptography;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Separation;
using IProfiler.Infrastructure.Imaging;
using IProfiler.Infrastructure.Ocr;
using Microsoft.Extensions.Logging;

/// <summary>
/// Task 1.4: skew correction, white ratio (blank at the default threshold) and orientation for every test page,
/// then a render-for-export with rotation + deskew + crop, checking the original's hash is unchanged.
///   IProfiler.Spike imaging tests/IProfiler.Tests/TestData
/// </summary>
internal static class ImagingSpike
{
    private static readonly string[] Pages =
    [
        "imaging/blank-g4.tif", "imaging/near-blank-staple-g4.tif", "imaging/near-blank-staple-gray.jpg",
        "imaging/sparse-text-g4.tif", "barcode/bw-g4-300dpi.tif", "imaging/skew-3deg-g4.tif",
        "imaging/upside-down-g4.tif", "ocr/invoice-150dpi.png", "patch/patch-T.tif",
    ];

    public static async Task<int> RunAsync(string dataDir)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
        var images = new OpenCvImageProcessor(loggerFactory.CreateLogger<OpenCvImageProcessor>());
        using var ocr = new PaddleOcrProvider(new AppPaths(AppContext.BaseDirectory, Path.GetTempPath()),
            loggerFactory.CreateLogger<PaddleOcrProvider>());
        var threshold = new BlankPageSettings().WhiteRatioThreshold;
        await ocr.DetectOrientationAsync(Path.Combine(dataDir, Pages[4])); // load the engine first

        Console.WriteLine($"{"page",-36} {"skew",7} {"ms",5} {"white",8} {"blank@" + threshold,10} {"ms",4}  orientation (upright/180, lines) ms");
        foreach (var name in Pages)
        {
            var path = Path.Combine(dataDir, name);
            var sw = Stopwatch.StartNew();
            var skew = await images.DetectSkewAngleAsync(path);
            var skewMs = sw.ElapsedMilliseconds;
            sw.Restart();
            var white = await images.GetWhiteRatioAsync(path);
            var whiteMs = sw.ElapsedMilliseconds;
            sw.Restart();
            var o = await ocr.DetectOrientationAsync(path);
            Console.WriteLine($"{name,-36} {skew,7:F2} {skewMs,5} {white,8:F5} {(white >= threshold ? "BLANK" : "content"),10} {whiteMs,4}  " +
                              $"{o.Rotation} ({o.UprightScore:F2}/{o.RotatedScore:F2}, {o.LinesSampled}) {sw.ElapsedMilliseconds}");
        }

        // Rotate a page in the model and export it: the original must be byte-identical afterwards.
        var original = Path.Combine(dataDir, "imaging/upside-down-g4.tif");
        var before = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(original)));
        var page = new Page { ImagePath = original, Rotation = PageRotation.Rotate180, DeskewAngle = 0.5, Crop = new PixelRect(100, 100, 2280, 3300) };
        var output = Path.Combine(Path.GetTempPath(), "iProfiler-spike", "export-upright.png");
        await images.RenderForExportAsync(page, output);
        var after = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(original)));
        Console.WriteLine($"export → {output}; original SHA-256 before {before[..16]}… after {after[..16]}… {(before == after ? "UNCHANGED" : "CHANGED!")}");
        return before == after ? 0 : 1;
    }
}
