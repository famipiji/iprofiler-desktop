using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IProfiler.Application.Pages;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Export;
using IProfiler.Core.Imaging;
using IProfiler.Core.Recognition;
using IProfiler.Core.Storage;
using IProfiler.Data;
using IProfiler.Infrastructure.Export;
using IProfiler.Infrastructure.Imaging;
using IProfiler.Infrastructure.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Task 1.5: analyse 4 pages (deskew + orientation), store the batch in SQLite, read it back from a fresh
/// context, OCR every page, and export one PDF-OCR + one multi-page TIFF from the stored metadata. Writes
/// manifest.json for tools/check-searchable-pdf.py, which checks phrase search in real PDF readers.
///   IProfiler.Spike export tests/IProfiler.Tests/TestData &lt;out dir&gt;
/// </summary>
internal static class ExportSpike
{
    // Upright, so it gets a crop (clear of the text, which spans x 300-2421, y 270-2203); the crop's offset is
    // subtracted from the expected word positions.
    private static readonly PixelRect UprightCrop = new(150, 200, 2300, 3200);

    public static async Task<int> RunAsync(string dataDir, string outDir)
    {
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        var paths = new AppPaths(AppContext.BaseDirectory, Path.Combine(outDir, "data"));
        Directory.CreateDirectory(paths.Data);

        var services = new ServiceCollection()
            .AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information)
                .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning))
            .AddSingleton(paths)
            .AddCaptureDatabase(paths)
            .AddSingleton<IOcrProvider, PaddleOcrProvider>()
            .AddSingleton<IImageProcessor, OpenCvImageProcessor>()
            .AddSingleton<IPageAnalyzer, PageAnalyzer>()
            .AddSingleton<IPdfAssembler, PdfSharpAssembler>()
            .AddSingleton<ITiffAssembler, LibTiffAssembler>()
            .BuildServiceProvider();
        await using var _ = services;
        var ocr = services.GetRequiredService<IOcrProvider>();
        var images = services.GetRequiredService<IImageProcessor>();
        var timings = new JsonObject();
        var sw = Stopwatch.StartNew();

        // 1. Pages as a scan/import would create them, then analysed (rotation + deskew as metadata).
        var files = new[]
        {
            Path.Combine(dataDir, "pdf", "phrases-skew-g4.tif"),
            Path.Combine(dataDir, "pdf", "phrases-g4.tif"),
            Path.Combine(dataDir, "pdf", "phrases-upside-down-200dpi.tif"),
            Path.Combine(dataDir, "barcode", "bw-g4-300dpi.tif"),
        };
        var hashes = files.ToDictionary(f => f, Hash);
        var batch = new Batch { Name = "Task 1.5 spike" };
        batch.Documents.Add(new Document { Order = 0, Pages = [NewPage(files[0], 0), NewPage(files[1], 1, UprightCrop)] });
        batch.Documents.Add(new Document { Order = 1, Pages = [NewPage(files[2], 0), NewPage(files[3], 1)] });
        var analyzer = services.GetRequiredService<IPageAnalyzer>();
        foreach (var page in batch.Documents.SelectMany(d => d.Pages))
            await analyzer.AnalyzeAsync(page);
        timings["analyseMs"] = sw.ElapsedMilliseconds;

        // 2. Store, then read back through a new context: everything below uses the stored metadata only.
        sw.Restart();
        await services.MigrateCaptureDatabaseAsync();
        await services.GetRequiredService<IBatchRepository>().AddAsync(batch);
        var stored = await services.GetRequiredService<IBatchRepository>().GetAsync(batch.Id)
                     ?? throw new InvalidOperationException("Batch not found after saving.");
        timings["sqliteMs"] = sw.ElapsedMilliseconds;
        var pages = stored.Documents.SelectMany(d => d.Pages).ToList();
        foreach (var p in pages)
            Console.WriteLine($"stored: {Path.GetFileName(p.ImagePath),-34} rotation {p.Rotation,-10} deskew {p.DeskewAngle,6:F2} crop {p.Crop?.ToString() ?? "-"}");

        // 3. OCR the page as it will be exported (cropped, straight, upright), then map the boxes back to original
        //    pixels. OCR of the original works for straight upright pages, but a turned page reads as garbage, and
        //    on a skewed page every line box is the axis-aligned bound of a tilted line (about twice as tall).
        sw.Restart();
        var exportPages = new List<ExportPage>();
        var ocrMs = new JsonArray();
        foreach (var p in pages)
        {
            var t = Stopwatch.StartNew();
            var upright = Path.Combine(paths.Temp, $"upright-{p.Id:N}.png");
            var geometry = await images.RenderForExportAsync(p, upright);
            var result = (await ocr.RecognizePageAsync(upright)).MapBoxes(geometry.ToOriginal);
            File.Delete(upright);

            ocrMs.Add(t.ElapsedMilliseconds);
            exportPages.Add(new ExportPage(p, result));
            Console.WriteLine($"--- OCR {Path.GetFileName(p.ImagePath)} ({t.ElapsedMilliseconds} ms):\n{result.Text}");
        }

        timings["ocrMs"] = ocrMs;

        // 4. Export.
        var pdfPath = Path.Combine(outDir, "batch.pdf");
        var tiffPath = Path.Combine(outDir, "batch.tif");
        sw.Restart();
        await services.GetRequiredService<IPdfAssembler>().CreatePdfAsync(exportPages, pdfPath);
        timings["pdfMs"] = sw.ElapsedMilliseconds;
        sw.Restart();
        await services.GetRequiredService<ITiffAssembler>().CreateMultiPageTiffAsync(pages, tiffPath);
        timings["tiffMs"] = sw.ElapsedMilliseconds;

        var unchanged = files.All(f => Hash(f) == hashes[f]);
        var manifest = new JsonObject
        {
            ["pdf"] = pdfPath,
            ["tiff"] = tiffPath,
            ["database"] = paths.Database,
            ["phrases"] = Path.Combine(dataDir, "pdf", "phrases.json"),
            ["originalsUnchanged"] = unchanged,
            ["timings"] = timings,
            ["pages"] = new JsonArray(pages.Select(p => (JsonNode)new JsonObject
            {
                ["file"] = Path.GetFileName(p.ImagePath),
                ["rotation"] = p.Rotation.ToString(),
                ["deskew"] = p.DeskewAngle,
                ["crop"] = p.Crop is { } c ? new JsonArray(c.X, c.Y, c.Width, c.Height) : null,
                ["ocrWords"] = exportPages.Single(e => e.Page.Id == p.Id).Ocr!.Lines.Sum(l => Math.Max(1, l.Words.Count)),
            }).ToArray()),
        };
        await File.WriteAllTextAsync(Path.Combine(outDir, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return unchanged ? 0 : 1;
    }

    private static Page NewPage(string file, int order, PixelRect? crop = null) =>
        new() { ImagePath = file, Order = order, Crop = crop };

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
