using IProfiler.Application.Separation;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;
using IProfiler.Core.Scanning;
using IProfiler.Core.Separation;
using IProfiler.Infrastructure.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace IProfiler.Tests.Separation;

/// <summary>
/// One test per <see cref="SeparationMode"/>, run on the canned <see cref="FakeBatch"/> through the fake providers.
/// The batch: keyword "INVOICE" on p01/p04/p07, barcode sheet p03, blank p05, Patch T sheet p06.
/// </summary>
public class SeparationEngineTests
{
    private static readonly PixelRect HeaderZone = new(0, 0, 2480, 400);

    // One sample per mode; the coverage test below fails if a new mode is added without one.
    public static IReadOnlyList<SeparationSettings> SettingsForEveryMode { get; } =
    [
        new PageCountSettings(4),
        new BarcodeSettings(),
        new BlankPageSettings(),
        new PatchCodeSettings(),
        new KeywordZoneSettings(HeaderZone, "invoice"),
    ];

    private readonly SeparationEngine _engine = new(
        new FakeBarcodeProvider(),
        new FakeImageProcessor(NullLogger<FakeImageProcessor>.Instance),
        new FakeOcrProvider(),
        NullLogger<SeparationEngine>.Instance);

    [Fact]
    public async Task PageCount_starts_a_new_document_every_n_pages()
    {
        var result = await SeparateAsync(new PageCountSettings(4));

        AssertDocuments(result, ["p01", "p02", "p03", "p04"], ["p05", "p06", "p07", "p08"]);
        Assert.Empty(result.RemovedSeparators);
    }

    [Fact]
    public async Task Barcode_splits_on_the_separator_sheet_and_removes_it()
    {
        var result = await SeparateAsync(new BarcodeSettings(BarcodeSymbology.Code39, TextPrefix: "SEP-"));

        AssertDocuments(result, ["p01", "p02"], ["p04", "p05", "p06", "p07", "p08"]);
        Assert.Equal(["p03"], Names(result.RemovedSeparators));
    }

    [Fact]
    public async Task Barcode_that_does_not_match_the_filter_does_not_split()
    {
        var result = await SeparateAsync(new BarcodeSettings(BarcodeSymbology.QrCode));

        AssertDocuments(result, ["p01", "p02", "p03", "p04", "p05", "p06", "p07", "p08"]);
    }

    [Fact]
    public async Task BlankPage_splits_on_the_blank_page_and_removes_it()
    {
        var result = await SeparateAsync(new BlankPageSettings(WhiteRatioThreshold: 0.99));

        AssertDocuments(result, ["p01", "p02", "p03", "p04"], ["p06", "p07", "p08"]);
        Assert.Equal(["p05"], Names(result.RemovedSeparators));
    }

    [Fact]
    public async Task PatchCode_splits_on_the_patch_sheet_and_can_keep_it_as_first_page()
    {
        var result = await SeparateAsync(new PatchCodeSettings(PatchCode.PatchT, RemoveSeparatorPage: false));

        AssertDocuments(result, ["p01", "p02", "p03", "p04", "p05"], ["p06", "p07", "p08"]);
        Assert.Empty(result.RemovedSeparators);
    }

    [Fact]
    public async Task KeywordZone_starts_a_new_document_at_each_keyword_page()
    {
        var result = await SeparateAsync(new KeywordZoneSettings(HeaderZone, "invoice"));

        AssertDocuments(result, ["p01", "p02", "p03"], ["p04", "p05", "p06"], ["p07", "p08"]);
        Assert.Empty(result.RemovedSeparators);
    }

    [Fact]
    public async Task Every_mode_has_settings_and_is_dispatched()
    {
        Assert.Equal(Enum.GetValues<SeparationMode>(), SettingsForEveryMode.Select(s => s.Mode).Order());

        foreach (var settings in SettingsForEveryMode)
        {
            var result = await SeparateAsync(settings);

            Assert.NotEmpty(result.Documents);
            Assert.All(result.Documents, d => Assert.NotEmpty(d.Pages));
        }
    }

    [Fact]
    public async Task Documents_and_pages_are_renumbered_in_order()
    {
        var result = await SeparateAsync(new BlankPageSettings());

        Assert.Equal([0, 1], result.Documents.Select(d => d.Order));
        Assert.All(result.Documents, d => Assert.Equal(Enumerable.Range(0, d.Pages.Count), d.Pages.Select(p => p.Order)));
    }

    private async Task<SeparationResult> SeparateAsync(SeparationSettings settings)
    {
        var temp = Path.Combine(Path.GetTempPath(), "iprofiler-tests");
        var scanner = new FakeScanProvider(new AppPaths(temp, temp), NullLogger<FakeScanProvider>.Instance);
        var pages = await scanner.ScanAsync(new ScanRequest(FakeScanProvider.DeviceId)).ToListAsync();

        return await _engine.SeparateAsync(pages, settings);
    }

    private static void AssertDocuments(SeparationResult result, params string[][] expected) =>
        Assert.Equal(expected, result.Documents.Select(d => Names(d.Pages)).ToArray());

    private static string[] Names(IEnumerable<Page> pages) =>
        pages.Select(p => Path.GetFileNameWithoutExtension(p.ImagePath)).ToArray();
}
