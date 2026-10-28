using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Infrastructure.Ocr;
using Microsoft.Extensions.Logging;

// Milestone 1 spike. Task 1.2: full-page OCR + one field-region OCR per sample, offline. Task 1.3: barcodes.
//   IProfiler.Spike ocr <samples dir> [report.json]
// The samples dir holds the images and expected.json (written by tools/make-ocr-samples.py).

if (args is ["barcodes", var barcodeDir, .. var patchFiles])
    return await BarcodeSpike.RunAsync(barcodeDir, patchFiles);

if (args is not ["ocr", var samplesDir, ..])
{
    Console.Error.WriteLine("usage: IProfiler.Spike ocr <samples dir> [report.json]");
    Console.Error.WriteLine("       IProfiler.Spike barcodes <samples dir> [extra patch-sheet files...]");
    return 2;
}

var reportPath = args.Length > 2 ? args[2] : null;
using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("Spike");

var paths = new AppPaths(AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "iProfiler-spike"));
var network = NetworkInterface.GetIsNetworkAvailable()
    && NetworkInterface.GetAllNetworkInterfaces().Any(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
log.LogInformation("Network available: {Network}", network);

var modelFiles = Directory.GetFiles(paths.Models, "*", SearchOption.AllDirectories);
var modelBytes = modelFiles.Sum(f => new FileInfo(f).Length);
log.LogInformation("Models: {Count} files, {MB:F1} MB in {Dir}", modelFiles.Length, modelBytes / 1048576.0, paths.Models);

var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(samplesDir, "expected.json")))!.AsObject();
var report = new JsonObject
{
    ["machine"] = Environment.MachineName,
    ["cpu"] = Environment.ProcessorCount,
    ["network"] = network,
    ["modelFiles"] = modelFiles.Length,
    ["modelMB"] = Math.Round(modelBytes / 1048576.0, 1),
};
var pages = new JsonArray();
var allOk = true;

using var ocr = new PaddleOcrProvider(paths, loggerFactory.CreateLogger<PaddleOcrProvider>());
var firstCall = true;
foreach (var (file, spec) in expected)
{
    var image = Path.Combine(samplesDir, file);
    var sw = Stopwatch.StartNew();
    var result = await ocr.RecognizePageAsync(image);
    var pageMs = sw.ElapsedMilliseconds;

    var words = spec!["words"]!.AsArray().Select(w => w!.GetValue<string>()).ToList();
    var missing = words.Where(w => !result.Text.Contains(w, StringComparison.Ordinal)).ToList();
    var entry = new JsonObject
    {
        ["file"] = file,
        ["firstCallIncludesModelLoad"] = firstCall,
        ["pageMs"] = pageMs,
        ["lines"] = result.Lines.Count,
        ["expectedWordsFound"] = $"{words.Count - missing.Count}/{words.Count}",
        ["missing"] = new JsonArray(missing.Select(m => (JsonNode)m!).ToArray()),
        ["text"] = result.Text,
    };
    firstCall = false;
    allOk &= missing.Count == 0;
    Console.WriteLine($"--- {file} ({pageMs} ms, {result.Lines.Count} lines)\n{result.Text}");

    if (spec["field"] is JsonObject field)
    {
        var r = field["region"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
        var region = new PixelRect(r[0], r[1], r[2], r[3]);
        sw.Restart();
        var fieldResult = await ocr.RecognizeRegionAsync(image, region);
        var want = field["text"]!.GetValue<string>();
        var got = fieldResult.Text.Trim();
        allOk &= got == want;
        entry["field"] = new JsonObject
        {
            ["region"] = $"{region}",
            ["expected"] = want,
            ["got"] = got,
            ["ok"] = got == want,
            ["ms"] = sw.ElapsedMilliseconds,
            ["box"] = fieldResult.Lines.Count > 0 ? $"{fieldResult.Lines[0].Box}" : null,
        };
        Console.WriteLine($"--- field {field["name"]} in {region}: \"{got}\" (expected \"{want}\") {sw.ElapsedMilliseconds} ms");
    }

    pages.Add(entry);
}

// Steady-state timing: OCR each page again now the engine is warm.
var warm = new JsonArray();
foreach (var (file, _) in expected)
{
    var sw = Stopwatch.StartNew();
    await ocr.RecognizePageAsync(Path.Combine(samplesDir, file));
    warm.Add(new JsonObject { ["file"] = file, ["pageMs"] = sw.ElapsedMilliseconds });
}

report["pages"] = pages;
report["warmRuns"] = warm;
report["ok"] = allOk;
var json = report.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
if (reportPath is not null)
    File.WriteAllText(reportPath, json);
Console.WriteLine(json);
return allOk ? 0 : 1;
