using IProfiler.Core.Domain;
using IProfiler.Core.Imaging;
using IProfiler.Core.Recognition;
using IProfiler.Core.Separation;
using Microsoft.Extensions.Logging;

namespace IProfiler.Application.Separation;

/// <summary>
/// The one implementation of <see cref="ISeparationEngine"/>. Every mode goes through the same
/// grouping loop; only the per-page decision differs, and that is chosen in exactly one place
/// (<see cref="ClassifierFor"/>).
/// </summary>
public sealed class SeparationEngine(
    IBarcodeProvider barcodes,
    IImageProcessor images,
    IOcrProvider ocr,
    ILogger<SeparationEngine> logger) : ISeparationEngine
{
    private enum PageRole
    {
        /// <summary>Belongs to the current document.</summary>
        Content,

        /// <summary>Starts a new document and is its first page.</summary>
        FirstPage,

        /// <summary>Ends the current document and is dropped.</summary>
        RemovedSeparator,
    }

    private delegate Task<PageRole> PageClassifier(Page page, int index, CancellationToken cancellationToken);

    public async Task<SeparationResult> SeparateAsync(
        IReadOnlyList<Page> pages,
        SeparationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var classify = ClassifierFor(settings);
        var documents = new List<Document>();
        var removed = new List<Page>();
        var current = new List<Page>();

        for (var i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pages[i];
            var role = await classify(page, i, cancellationToken);

            if (role != PageRole.Content)
                Flush();

            if (role == PageRole.RemovedSeparator)
                removed.Add(page);
            else
                current.Add(page);
        }

        Flush();

        logger.LogInformation(
            "Separated {PageCount} pages into {DocumentCount} documents ({Mode}, {RemovedCount} separator pages removed)",
            pages.Count, documents.Count, settings.Mode, removed.Count);

        return new SeparationResult(documents, removed);

        void Flush()
        {
            if (current.Count == 0)
                return;

            for (var order = 0; order < current.Count; order++)
                current[order].Order = order;

            documents.Add(new Document { Order = documents.Count, Pages = current });
            current = [];
        }
    }

    // The single SeparationMode dispatch (CLAUDE.md rule 4).
    private PageClassifier ClassifierFor(SeparationSettings settings) => settings switch
    {
        PageCountSettings s => PageCount(s),
        BarcodeSettings s => Barcode(s),
        BlankPageSettings s => BlankPage(s),
        PatchCodeSettings s => PatchCode(s),
        KeywordZoneSettings s => KeywordZone(s),
        _ => throw new NotSupportedException($"No separation classifier for {settings.Mode}."),
    };

    private static PageClassifier PageCount(PageCountSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.PagesPerDocument, 1);

        return (_, index, _) => Task.FromResult(
            index % settings.PagesPerDocument == 0 ? PageRole.FirstPage : PageRole.Content);
    }

    private PageClassifier Barcode(BarcodeSettings settings) => async (page, _, cancellationToken) =>
    {
        var found = await barcodes.ReadPageAsync(page.ImagePath, cancellationToken);
        var isSeparator = found.Any(b =>
            (settings.Symbology is null || b.Symbology == settings.Symbology) &&
            (settings.TextPrefix is null || b.Text.StartsWith(settings.TextPrefix, StringComparison.Ordinal)));

        return SeparatorRole(isSeparator, settings.RemoveSeparatorPage);
    };

    private PageClassifier BlankPage(BlankPageSettings settings) => async (page, _, cancellationToken) =>
    {
        var whiteRatio = await images.GetWhiteRatioAsync(page.ImagePath, cancellationToken);
        return SeparatorRole(whiteRatio >= settings.WhiteRatioThreshold, settings.RemoveSeparatorPage);
    };

    private static PageClassifier PatchCode(PatchCodeSettings settings) => (page, _, _) =>
        Task.FromResult(SeparatorRole(page.PatchCode == settings.SeparatorPatch, settings.RemoveSeparatorPage));

    private PageClassifier KeywordZone(KeywordZoneSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Keyword);

        return async (page, _, cancellationToken) =>
        {
            var text = await ocr.RecognizeRegionAsync(page.ImagePath, settings.Zone, cancellationToken);
            return text.Text.Contains(settings.Keyword, StringComparison.OrdinalIgnoreCase)
                ? PageRole.FirstPage
                : PageRole.Content;
        };
    }

    private static PageRole SeparatorRole(bool isSeparator, bool remove) =>
        !isSeparator ? PageRole.Content
        : remove ? PageRole.RemovedSeparator
        : PageRole.FirstPage;
}
