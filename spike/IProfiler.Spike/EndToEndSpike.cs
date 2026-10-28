using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IProfiler.Application.Pages;
using IProfiler.Application.Separation;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Export;
using IProfiler.Core.Imaging;
using IProfiler.Core.Recognition;
using IProfiler.Core.Scanning;
using IProfiler.Core.Separation;
using IProfiler.Core.Storage;
using IProfiler.Data;
using IProfiler.Infrastructure.Barcodes;
using IProfiler.Infrastructure.Export;
using IProfiler.Infrastructure.Imaging;
using IProfiler.Infrastructure.Import;
using IProfiler.Infrastructure.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Task 1.6, import mode (Task 1.1 deferred): the whole capture flow on files instead of a scan. Import the sample
/// pages (copied untouched into AppPaths.Scans) → analyse each (orientation + skew, white ratio, barcodes, patch
/// code) → OCR the invoice-number field → separate (Code39 "SEP-" starts a document) → store in SQLite and read back
/// → OCR each page for the text layer → export one PDF-OCR and one multi-page TIFF from the stored metadata.
/// Writes report.json (ok = every check passed) to the out dir. Run it from the install dir to prove the
/// installed models and native runtimes work, e.g. in Windows Sandbox with networking disabled.
///   IProfiler.Spike e2e &lt;samples dir&gt; &lt;out dir&gt;
/// The samples dir is samples/spike (tools/make-*-samples.py): expected.json and bw-g4-300dpi.json hold the answers.
/// </summary>
internal static class EndToEndSpike
{
    // Separator page last but one, so separation gives two documents.
    private static readonly string[] PreferredOrder = ["gen-invoice-300dpi.tif", "gen-letter-200dpi.jpg", "bw-g4-300dpi.tif", "patch-T.tif"];
    private static readonly string[] ImageExtensions = [".tif", ".tiff", ".jpg", ".jpeg", ".png", ".bmp"];

    public static async Task<int> RunAsync(string samplesDir, string outDir)
    {
        var total = Stopwatch.StartNew();
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        var paths = new AppPaths(AppContext.BaseDirectory, Path.Combine(outDir, "data"));
        Directory.CreateDirectory(paths.Data);

        var services = new ServiceCollection()
            .AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information)
                .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning))
            .AddSingleton(paths)
            .AddCaptureDatabase(paths)
            .AddSingleton<IImportProvider, FileImportProvider>()
            .AddSingleton<IOcrProvider, PaddleOcrProvider>()
            .AddSingleton<IBarcodeProvider, ZXingBarcodeProvider>()
            .AddSingleton<IPatchCodeDetector, OpenCvPatchCodeDetector>()
            .AddSingleton<IImageProcessor, OpenCvImageProcessor>()
            .AddSingleton<IPageAnalyzer, PageAnalyzer>()
            .AddSingleton<ISeparationEngine, SeparationEngine>()
            .AddSingleton<IPdfAssembler, PdfSharpAssembler>()
            .AddSingleton<ITiffAssembler, LibTiffAssembler>()
            .BuildServiceProvider();
        await using var _ = services;
        var ocr = services.GetRequiredService<IOcrProvider>();
        var barcodes = services.GetRequiredService<IBarcodeProvider>();
        var images = services.GetRequiredService<IImageProcessor>();

        var checks = new JsonObject();
        var timings = new JsonObject();
        var report = new JsonObject
        {
            ["machine"] = Environment.MachineName,
            ["cpu"] = Environment.ProcessorCount,
            ["installDir"] = paths.Install,
            ["network"] = NetworkUp(),
            ["modelMB"] = Math.Round(Directory.GetFiles(paths.Models, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1048576.0, 1),
        };

        // Answers: words per file, the invoice field, and the barcodes on the delivery note.
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(samplesDir, "expected.json")))!.AsObject();
        var note = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(samplesDir, "bw-g4-300dpi.json")))!.AsObject();
        var expectedWords = expected.ToDictionary(e => e.Key, e => Strings(e.Value!["words"]!));
        expectedWords["bw-g4-300dpi.tif"] = Strings(note["words"]!);

        // 1. Import.
        var sources = PreferredOrder.Select(f => Path.Combine(samplesDir, f)).Where(File.Exists)
            .Concat(Directory.GetFiles(samplesDir).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())
                                                               && !PreferredOrder.Contains(Path.GetFileName(f))).Order())
            .ToList();
        var sourceHashes = sources.ToDictionary(f => f, Hash);
        var sw = Stopwatch.StartNew();
        var pages = new List<Page>();
        await foreach (var page in services.GetRequiredService<IImportProvider>().ImportAsync(sources))
            pages.Add(page);
        timings["importMs"] = sw.ElapsedMilliseconds;
        var sourceOf = pages.ToDictionary(p => p.Id, p => sources[p.Order]);
        checks["importedCopiesIdentical"] = pages.All(p => Hash(p.ImagePath) == sourceHashes[sourceOf[p.Id]]);

        // 2. Analyse each page: orientation + skew (PageAnalyzer), white ratio, barcodes, software patch code.
        var analyzer = services.GetRequiredService<IPageAnalyzer>();
        var patches = services.GetRequiredService<IPatchCodeDetector>();
        var pageReports = new Dictionary<Guid, JsonObject>();
        var barcodeHits = 0;
        var barcodeExpected = note["barcodes"]!.AsArray();
        foreach (var page in pages)
        {
            var file = Path.GetFileName(sourceOf[page.Id]);
            var t = Stopwatch.StartNew();
            await analyzer.AnalyzeAsync(page);
            var analyseMs = t.ElapsedMilliseconds;
            t.Restart();
            var white = await images.GetWhiteRatioAsync(page.ImagePath);
            var whiteMs = t.ElapsedMilliseconds;
            t.Restart();
            var found = await barcodes.ReadPageAsync(page.ImagePath);
            var barcodeMs = t.ElapsedMilliseconds;
            t.Restart();
            var patch = await patches.DetectAsync(page.ImagePath);
            var patchMs = t.ElapsedMilliseconds;

            if (file == "bw-g4-300dpi.tif")
                barcodeHits = barcodeExpected.Count(e => found.Any(b => b.Symbology.ToString() == e!["symbology"]!.GetValue<string>()
                                                                       && b.Text == e["text"]!.GetValue<string>()));

            pageReports[page.Id] = new JsonObject
            {
                ["file"] = file,
                ["dpi"] = page.Dpi,
                ["rotation"] = page.Rotation.ToString(),
                ["deskew"] = Math.Round(page.DeskewAngle ?? 0, 2),
                ["whiteRatio"] = Math.Round(white, 5),
                ["barcodes"] = new JsonArray(found.Select(b => (JsonNode)$"{b.Symbology}:{b.Text}").ToArray()),
                ["patchCode"] = patch?.ToString(),
                ["ms"] = new JsonObject { ["orientationAndSkew"] = analyseMs, ["whiteRatio"] = whiteMs, ["barcodes"] = barcodeMs, ["patchCode"] = patchMs },
            };
            Console.WriteLine($"page {page.Order} {file}: {page.Rotation}, deskew {page.DeskewAngle:F2}, white {white:F5}, " +
                              $"{found.Count} barcodes, patch {patch?.ToString() ?? "-"} ({analyseMs}+{whiteMs}+{barcodeMs}+{patchMs} ms)");
        }

        checks["barcodes"] = $"{barcodeHits}/{barcodeExpected.Count}";
        checks["patchTFound"] = pages.Any(p => pageReports[p.Id]["file"]!.GetValue<string>() == "patch-T.tif" && pageReports[p.Id]["patchCode"]?.GetValue<string>() == "PatchT");

        // 3. OCR one field region (FlyOCR): the invoice number.
        var invoice = pages.Single(p => Path.GetFileName(sourceOf[p.Id]) == "gen-invoice-300dpi.tif");
        var field = expected["gen-invoice-300dpi.tif"]!["field"]!;
        var r = field["region"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
        sw.Restart();
        var fieldText = (await ocr.RecognizeRegionAsync(invoice.ImagePath, new PixelRect(r[0], r[1], r[2], r[3]))).Text.Trim();
        timings["fieldOcrMs"] = sw.ElapsedMilliseconds;
        checks["field"] = new JsonObject { ["expected"] = field["text"]!.GetValue<string>(), ["got"] = fieldText };
        Console.WriteLine($"field {field["name"]}: \"{fieldText}\" ({sw.ElapsedMilliseconds} ms)");

        // 4. Separation through the single engine: the Code39 "SEP-" page starts a new document and is kept.
        sw.Restart();
        var separation = await services.GetRequiredService<ISeparationEngine>().SeparateAsync(
            pages, new BarcodeSettings(BarcodeSymbology.Code39, "SEP-", RemoveSeparatorPage: false));
        timings["separationMs"] = sw.ElapsedMilliseconds;
        checks["documents"] = separation.Documents.Count;

        // 5. Store, then read back through a new context: export uses the stored metadata only.
        sw.Restart();
        var batch = new Batch { Name = "Task 1.6 end-to-end (import)" };
        foreach (var document in separation.Documents)
            batch.Documents.Add(document);
        await services.MigrateCaptureDatabaseAsync();
        await services.GetRequiredService<IBatchRepository>().AddAsync(batch);
        var stored = await services.GetRequiredService<IBatchRepository>().GetAsync(batch.Id)
                     ?? throw new InvalidOperationException("Batch not found after saving.");
        timings["sqliteMs"] = sw.ElapsedMilliseconds;
        var storedPages = stored.Documents.OrderBy(d => d.Order).SelectMany(d => d.Pages.OrderBy(p => p.Order)).ToList();
        checks["storedPages"] = storedPages.Count;
        // SQLite keeps recent writes in the -wal file next to the database, so check and count both.
        var databaseFiles = new[] { paths.Database, paths.Database + "-wal" }.Where(File.Exists).ToList();
        checks["databaseMetadataOnly"] = !databaseFiles.Any(ContainsImage);
        report["databaseKB"] = databaseFiles.Sum(f => new FileInfo(f).Length) / 1024;

        // 6. OCR each page as exported (upright, straight) for the text layer; boxes mapped back to original pixels.
        var exportPages = new List<ExportPage>();
        var ocrMs = new JsonArray();
        var wordsFound = 0;
        var wordsTotal = 0;
        foreach (var p in storedPages)
        {
            var t = Stopwatch.StartNew();
            var upright = Path.Combine(paths.Temp, $"upright-{p.Id:N}.png");
            Directory.CreateDirectory(paths.Temp);
            var geometry = await images.RenderForExportAsync(p, upright);
            var result = (await ocr.RecognizePageAsync(upright)).MapBoxes(geometry.ToOriginal);
            File.Delete(upright);
            ocrMs.Add(t.ElapsedMilliseconds);
            exportPages.Add(new ExportPage(p, result));

            var pageReport = pageReports[p.Id];
            pageReport["ocrLines"] = result.Lines.Count;
            if (expectedWords.TryGetValue(pageReport["file"]!.GetValue<string>(), out var words))
            {
                var missing = words.Where(w => !result.Text.Contains(w, StringComparison.Ordinal)).ToList();
                wordsFound += words.Count - missing.Count;
                wordsTotal += words.Count;
                pageReport["expectedWords"] = $"{words.Count - missing.Count}/{words.Count}";
                pageReport["missingWords"] = new JsonArray(missing.Select(m => (JsonNode)m!).ToArray());
            }
        }

        timings["pageOcrMs"] = ocrMs;
        checks["ocrWords"] = $"{wordsFound}/{wordsTotal}";

        // 7. Export.
        var pdfPath = Path.Combine(outDir, "batch.pdf");
        var tiffPath = Path.Combine(outDir, "batch.tif");
        sw.Restart();
        await services.GetRequiredService<IPdfAssembler>().CreatePdfAsync(exportPages, pdfPath);
        timings["pdfMs"] = sw.ElapsedMilliseconds;
        sw.Restart();
        await services.GetRequiredService<ITiffAssembler>().CreateMultiPageTiffAsync(storedPages, tiffPath);
        timings["tiffMs"] = sw.ElapsedMilliseconds;
        report["pdfKB"] = new FileInfo(pdfPath).Length / 1024;
        report["tiffKB"] = new FileInfo(tiffPath).Length / 1024;

        checks["originalsUnchanged"] = sources.All(f => Hash(f) == sourceHashes[f])
                                       && pages.All(p => Hash(p.ImagePath) == sourceHashes[sourceOf[p.Id]]);
        timings["totalMs"] = total.ElapsedMilliseconds;

        // Which VC++ runtime copies the native OCR libraries loaded: the installer ships them next to the exe.
        report["vcRuntime"] = new JsonArray(Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Where(m => m.ModuleName.ToLowerInvariant() is "msvcp140.dll" or "vcruntime140.dll" or "vcruntime140_1.dll" or "vcomp140.dll")
            .Select(m => (JsonNode)m.FileName!).ToArray());

        var ok = checks["importedCopiesIdentical"]!.GetValue<bool>()
                 && barcodeHits == barcodeExpected.Count
                 && checks["patchTFound"]!.GetValue<bool>()
                 && fieldText == field["text"]!.GetValue<string>()
                 && separation.Documents.Count == 2
                 && storedPages.Count == pages.Count
                 && checks["databaseMetadataOnly"]!.GetValue<bool>()
                 && wordsFound == wordsTotal
                 && checks["originalsUnchanged"]!.GetValue<bool>();
        report["ok"] = ok;
        report["checks"] = checks;
        report["timings"] = timings;
        report["pages"] = new JsonArray(storedPages.Select(p => (JsonNode)pageReports[p.Id].DeepClone()).ToArray());
        report["pdf"] = pdfPath;
        report["tiff"] = tiffPath;

        var json = report.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outDir, "report.json"), json);
        Console.WriteLine(json);
        return ok ? 0 : 1;
    }

    private static List<string> Strings(JsonNode array) => array.AsArray().Select(w => w!.GetValue<string>()).ToList();

    private static bool NetworkUp() =>
        NetworkInterface.GetIsNetworkAvailable()
        && NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

    // TIFF (both byte orders), JPEG and PNG signatures anywhere in a database file.
    private static bool ContainsImage(string databasePath)
    {
        // Shared read: EF Core's connection pool still has the file open.
        using var file = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        byte[][] signatures = [[0x49, 0x49, 0x2A, 0x00], [0x4D, 0x4D, 0x00, 0x2A], [0xFF, 0xD8, 0xFF], [0x89, .. Encoding.ASCII.GetBytes("PNG")]];
        return signatures.Any(s => bytes.AsSpan().IndexOf(s) >= 0);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
