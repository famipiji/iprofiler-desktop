using System.Runtime.CompilerServices;
using BitMiracle.LibTiff.Classic;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Scanning;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Import;

/// <summary>
/// Imports image files as pages. Each original is copied into its own folder under <see cref="AppPaths.Scans"/>,
/// because the source may be a hot folder, network share or USB stick that won't stay. Single-image files (TIFF,
/// JPEG, PNG, BMP) are copied byte for byte. A multi-page TIFF is split into one single-page TIFF per page with
/// LibTiff.NET (BSD) by copying each page's tags and compressed strips or tiles as they are, so the pixels are
/// never decoded or re-encoded. PDF import is not done yet (M2).
/// </summary>
public sealed class FileImportProvider(AppPaths paths, ILogger<FileImportProvider> logger) : IImportProvider
{
    private static readonly HashSet<string> CopiedAsIs = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
    private static readonly HashSet<string> TiffExtensions = new(StringComparer.OrdinalIgnoreCase) { ".tif", ".tiff" };

    // Everything a page needs to decode the same; PAGENUMBER and SUBFILETYPE are left out (the copy is one page).
    private static readonly TiffTag[] PageTags =
    [
        TiffTag.IMAGEWIDTH, TiffTag.IMAGELENGTH, TiffTag.BITSPERSAMPLE, TiffTag.SAMPLESPERPIXEL, TiffTag.COMPRESSION,
        TiffTag.PHOTOMETRIC, TiffTag.FILLORDER, TiffTag.ORIENTATION, TiffTag.PLANARCONFIG, TiffTag.ROWSPERSTRIP,
        TiffTag.TILEWIDTH, TiffTag.TILELENGTH, TiffTag.XRESOLUTION, TiffTag.YRESOLUTION, TiffTag.RESOLUTIONUNIT,
        TiffTag.T4OPTIONS, TiffTag.T6OPTIONS, TiffTag.PREDICTOR, TiffTag.COLORMAP, TiffTag.JPEGTABLES,
        TiffTag.EXTRASAMPLES, TiffTag.SAMPLEFORMAT, TiffTag.YCBCRSUBSAMPLING, TiffTag.YCBCRPOSITIONING,
        TiffTag.REFERENCEBLACKWHITE, TiffTag.SOFTWARE, TiffTag.DATETIME, TiffTag.MAKE, TiffTag.MODEL,
    ];

    public async IAsyncEnumerable<Page> ImportAsync(
        IReadOnlyList<string> filePaths,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(paths.Scans, $"import-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(folder);
        var order = 0;

        foreach (var source in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(source);
            var name = Path.GetFileNameWithoutExtension(source);

            if (CopiedAsIs.Contains(extension))
            {
                var copy = Path.Combine(folder, $"{order:D4}-{name}{extension.ToLowerInvariant()}");
                await CopyAsync(source, copy, cancellationToken);
                logger.LogInformation("Imported {Source} as page {Order}", source, order);
                yield return new Page { ImagePath = copy, Order = order++ };
            }
            else if (TiffExtensions.Contains(extension))
            {
                foreach (var page in await ImportTiffAsync(source, folder, name, order, cancellationToken))
                {
                    order++;
                    yield return page;
                }
            }
            else
            {
                throw new NotSupportedException($"Can't import {source}: only TIFF, JPEG, PNG and BMP files are supported for now.");
            }
        }
    }

    private async Task<IReadOnlyList<Page>> ImportTiffAsync(string source, string folder, string name, int firstOrder, CancellationToken cancellationToken)
    {
        TiffMessages.Install();
        short pageCount;
        using (var probe = Tiff.Open(source, "r") ?? throw Failure("open", source))
            pageCount = probe.NumberOfDirectories();

        if (pageCount == 1)
        {
            var copy = Path.Combine(folder, $"{firstOrder:D4}-{name}.tif");
            await CopyAsync(source, copy, cancellationToken);
            logger.LogInformation("Imported {Source} as page {Order}", source, firstOrder);
            return [NewTiffPage(copy, firstOrder)];
        }

        // LibTiff is synchronous; splitting a long TIFF shouldn't hold up the caller's thread.
        return await Task.Run(() =>
        {
            var pages = new List<Page>(pageCount);
            using var input = Tiff.Open(source, "r") ?? throw Failure("open", source);
            for (short i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var copy = Path.Combine(folder, $"{firstOrder + i:D4}-{name}-p{i + 1}.tif");
                CopyTiffPage(input, i, copy);
                pages.Add(NewTiffPage(copy, firstOrder + i));
            }

            logger.LogInformation("Imported {Source}: {Pages} pages (split without re-encoding) as pages {First}-{Last}",
                source, pageCount, firstOrder, firstOrder + pageCount - 1);
            return (IReadOnlyList<Page>)pages;
        }, cancellationToken);
    }

    private static Page NewTiffPage(string path, int order) =>
        new() { ImagePath = path, Order = order, Dpi = ImageFile.TiffDpi(path) is { } dpi ? (int)Math.Round(dpi) : null };

    private static void CopyTiffPage(Tiff input, short directory, string destination)
    {
        if (!input.SetDirectory(directory))
            throw Failure($"read page {directory + 1} of", input.FileName());

        var compression = (Compression)(input.GetField(TiffTag.COMPRESSION)?[0].ToInt() ?? (int)Compression.NONE);
        if (compression == Compression.OJPEG)
            throw new NotSupportedException($"{input.FileName()} page {directory + 1}: old-style JPEG TIFFs can't be split without re-encoding.");

        using (var output = Tiff.Open(destination, "w") ?? throw Failure("create", destination))
        {
            foreach (var tag in PageTags)
            {
                if (input.GetField(tag) is { } values)
                    output.SetField(tag, values.Select(v => v.Value).ToArray());
            }

            var tiled = input.IsTiled();
            var chunks = tiled ? input.NumberOfTiles() : input.NumberOfStrips();
            for (var c = 0; c < chunks; c++)
            {
                var size = (int)(tiled ? input.RawTileSize(c) : input.RawStripSize(c));
                var buffer = new byte[size];
                var read = tiled ? input.ReadRawTile(c, buffer, 0, size) : input.ReadRawStrip(c, buffer, 0, size);
                var written = read < 0 ? -1 : tiled ? output.WriteRawTile(c, buffer, read) : output.WriteRawStrip(c, buffer, read);
                if (read < 0 || written < 0)
                    throw Failure($"copy {(tiled ? "tile" : "strip")} {c} of page {directory + 1} of", input.FileName());
            }

            if (!output.WriteDirectory())
                throw Failure("write", destination);
        }
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static IOException Failure(string action, string path) =>
        new($"Could not {action} TIFF {path}: {TiffMessages.LastError ?? "unknown LibTiff error"}");
}
