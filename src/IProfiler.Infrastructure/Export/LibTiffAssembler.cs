using System.Diagnostics;
using BitMiracle.LibTiff.Classic;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Export;
using IProfiler.Core.Imaging;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Export;

/// <summary>
/// Multi-page TIFF export with LibTiff.NET (BSD). Each page is rendered through
/// <see cref="IImageProcessor.RenderForExportAsync"/> (crop, deskew, rotation) and appended as one page:
/// bilevel pages as 1-bit CCITT G4 (what scanners and M-Files viewers expect), greyscale and colour as LZW,
/// all tagged with the page's real resolution and page number.
/// </summary>
public sealed class LibTiffAssembler(IImageProcessor images, AppPaths paths, ILogger<LibTiffAssembler> logger) : ITiffAssembler
{
    public async Task CreateMultiPageTiffAsync(IReadOnlyList<Page> pages, string outputPath, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var work = new ExportWorkFolder(paths, "tiff", logger);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        TiffMessages.Install();
        using (var output = Tiff.Open(outputPath, "w") ?? throw Failure("create", outputPath))
        {
            for (var i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pages[i];
                var rendered = work.PageFile(i, ".tif");
                var geometry = await images.RenderForExportAsync(page, rendered, cancellationToken);
                var dpi = ImageFile.Dpi(page.ImagePath, geometry.OriginalWidth, geometry.OriginalHeight, page.Dpi);
                var kind = AppendPage(output, rendered, dpi, i, pages.Count);
                logger.LogInformation("TIFF page {Number}: {File} {Width}x{Height} px, {Kind}, {Dpi:F0} dpi",
                    i + 1, Path.GetFileName(page.ImagePath), geometry.Width, geometry.Height, kind, dpi);
            }
        }

        logger.LogInformation("TIFF {Output}: {Pages} pages, {KB} KB in {ElapsedMs} ms",
            outputPath, pages.Count, new FileInfo(outputPath).Length / 1024, sw.ElapsedMilliseconds);
    }

    /// <summary>Copies the single-page render into the next directory of <paramref name="output"/>.</summary>
    private static string AppendPage(Tiff output, string rendered, double dpi, int index, int count)
    {
        using var input = Tiff.Open(rendered, "r") ?? throw Failure("read", rendered);
        var width = input.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        var height = input.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        var samples = input.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;
        var bits = input.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 1;
        var photometric = (Photometric)input.GetField(TiffTag.PHOTOMETRIC)[0].ToInt();
        if (bits != 8 || !(samples == 1 && photometric == Photometric.MINISBLACK || samples == 3 && photometric == Photometric.RGB))
            throw new NotSupportedException($"Rendered page {rendered}: {samples} x {bits}-bit {photometric} is not supported.");

        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            rows[y] = new byte[input.ScanlineSize()];
            if (!input.ReadScanline(rows[y], y))
                throw Failure($"read row {y} of", rendered);
        }

        var bilevel = samples == 1 && rows.All(r => r.All(v => v is 0 or 255));

        output.SetField(TiffTag.SUBFILETYPE, FileType.PAGE);
        output.SetField(TiffTag.PAGENUMBER, index, count);
        output.SetField(TiffTag.IMAGEWIDTH, width);
        output.SetField(TiffTag.IMAGELENGTH, height);
        output.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        output.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        output.SetField(TiffTag.XRESOLUTION, dpi);
        output.SetField(TiffTag.YRESOLUTION, dpi);
        output.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.INCH);
        output.SetField(TiffTag.SOFTWARE, "iProfiler Desktop");
        output.SetField(TiffTag.SAMPLESPERPIXEL, samples);

        if (bilevel)
        {
            // 1 bit per pixel, 1 = black (MinIsWhite), one strip: the usual shape of a G4 scan.
            output.SetField(TiffTag.BITSPERSAMPLE, 1);
            output.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
            output.SetField(TiffTag.COMPRESSION, Compression.CCITTFAX4);
            output.SetField(TiffTag.ROWSPERSTRIP, height);
            var packed = new byte[(width + 7) / 8];
            for (var y = 0; y < height; y++)
            {
                Array.Clear(packed);
                for (var x = 0; x < width; x++)
                {
                    if (rows[y][x] == 0)
                        packed[x >> 3] |= (byte)(0x80 >> (x & 7));
                }

                if (!output.WriteScanline(packed, y))
                    throw Failure($"write row {y} of", rendered);
            }

            output.WriteDirectory();
            return "1-bit G4";
        }

        output.SetField(TiffTag.BITSPERSAMPLE, 8);
        output.SetField(TiffTag.PHOTOMETRIC, samples == 1 ? Photometric.MINISBLACK : Photometric.RGB);
        output.SetField(TiffTag.COMPRESSION, Compression.LZW);
        output.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);
        output.SetField(TiffTag.ROWSPERSTRIP, output.DefaultStripSize(0));
        for (var y = 0; y < height; y++)
        {
            if (!output.WriteScanline(rows[y], y))
                throw Failure($"write row {y} of", rendered);
        }

        output.WriteDirectory();
        return samples == 1 ? "8-bit grey LZW" : "24-bit RGB LZW";
    }

    private static IOException Failure(string action, string path) =>
        new($"LibTiff could not {action} {path}: {TiffMessages.LastError ?? "no details"}");
}
