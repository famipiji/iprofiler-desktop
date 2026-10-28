using System.Diagnostics;
using System.Text.Json.Nodes;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Infrastructure.Barcodes;
using IProfiler.Infrastructure.Ocr;
using Microsoft.Extensions.Logging;

/// <summary>
/// Task 1.3: barcodes / QR on the 1-bit G4 page (full page + each drawn region), PaddleOCR on the same page,
/// and the software patch-code check on patch sheets. Prints timings.
///   IProfiler.Spike barcodes samples/spike [extra patch-sheet files...]
/// </summary>
internal static class BarcodeSpike
{
    public static async Task<int> RunAsync(string samplesDir, string[] extraPatchFiles)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        var barcodes = new ZXingBarcodeProvider(loggerFactory.CreateLogger<ZXingBarcodeProvider>());
        var patches = new OpenCvPatchCodeDetector(loggerFactory.CreateLogger<OpenCvPatchCodeDetector>());
        var page = Path.Combine(samplesDir, "bw-g4-300dpi.tif");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(samplesDir, "bw-g4-300dpi.json")))!;
        var ok = true;

        for (var run = 1; run <= 2; run++)
        {
            var sw = Stopwatch.StartNew();
            var all = await barcodes.ReadPageAsync(page);
            Console.WriteLine($"[run {run}] page: {all.Count} barcodes in {sw.ElapsedMilliseconds} ms: " +
                              string.Join("; ", all.Select(r => $"{r.Symbology} \"{r.Text}\" {r.Box}")));
            ok &= all.Count == 3;
        }

        foreach (var b in meta["barcodes"]!.AsArray())
        {
            var r = b!["region"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
            var sw = Stopwatch.StartNew();
            var found = await barcodes.ReadRegionAsync(page, new PixelRect(r[0], r[1], r[2], r[3]));
            var want = b["text"]!.GetValue<string>();
            var good = found.Count == 1 && found[0].Text == want && found[0].Symbology.ToString() == b["symbology"]!.GetValue<string>();
            ok &= good;
            Console.WriteLine($"region {b["symbology"]}: {(good ? "OK" : "FAIL")} \"{string.Join("|", found.Select(f => f.Text))}\" in {sw.ElapsedMilliseconds} ms");
        }

        using (var ocr = new PaddleOcrProvider(new AppPaths(AppContext.BaseDirectory, Path.GetTempPath()),
                   loggerFactory.CreateLogger<PaddleOcrProvider>()))
        {
            await ocr.RecognizePageAsync(page); // load + warm up
            var sw = Stopwatch.StartNew();
            var text = (await ocr.RecognizePageAsync(page)).Text;
            var words = meta["words"]!.AsArray().Select(w => w!.GetValue<string>()).ToList();
            var missing = words.Where(w => !text.Contains(w, StringComparison.Ordinal)).ToList();
            ok &= missing.Count == 0;
            Console.WriteLine($"OCR on G4 page (warm): {words.Count - missing.Count}/{words.Count} words in {sw.ElapsedMilliseconds} ms" +
                              (missing.Count > 0 ? $"; missing {string.Join(", ", missing)}" : ""));
        }

        foreach (var file in new[] { Path.Combine(samplesDir, "patch-T.tif"), page }.Concat(extraPatchFiles))
        {
            var sw = Stopwatch.StartNew();
            var code = await patches.DetectAsync(file);
            Console.WriteLine($"patch {Path.GetFileName(file)}: {code?.ToString() ?? "none"} in {sw.ElapsedMilliseconds} ms");
        }

        Console.WriteLine(ok ? "RESULT: OK" : "RESULT: FAIL");
        return ok ? 0 : 1;
    }
}
