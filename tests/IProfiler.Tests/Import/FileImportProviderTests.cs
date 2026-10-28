using System.Security.Cryptography;
using BitMiracle.LibTiff.Classic;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Infrastructure.Export;
using IProfiler.Infrastructure.Imaging;
using IProfiler.Infrastructure.Import;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace IProfiler.Tests.Import;

public sealed class FileImportProviderTests : IDisposable
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "TestData");
    private static readonly string NoteG4 = Path.Combine(Data, "barcode", "bw-g4-300dpi.tif"); // 300 dpi tag
    private static readonly string StapleJpeg = Path.Combine(Data, "imaging", "near-blank-staple-gray.jpg");

    private readonly AppPaths _paths = new(
        AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "iProfiler-tests", Guid.NewGuid().ToString("N")));

    private FileImportProvider Importer => new(_paths, NullLogger<FileImportProvider>.Instance);

    [Fact]
    public async Task Single_image_files_are_copied_byte_for_byte_under_the_scans_folder()
    {
        var pages = await ImportAsync(NoteG4, StapleJpeg);

        Assert.Equal(2, pages.Count);
        Assert.Equal([0, 1], pages.Select(p => p.Order));
        Assert.All(pages, p => Assert.StartsWith(_paths.Scans, p.ImagePath));
        Assert.Equal(Hash(NoteG4), Hash(pages[0].ImagePath));
        Assert.Equal(Hash(StapleJpeg), Hash(pages[1].ImagePath));
        Assert.Equal(300, pages[0].Dpi); // from the TIFF tag
        Assert.Null(pages[1].Dpi); // JPEG: not read yet, export estimates it
    }

    [Fact]
    public async Task A_multi_page_tiff_is_split_into_pages_without_re_encoding()
    {
        // Two pages: 1-bit G4 and 8-bit grey LZW, as the TIFF export writes them.
        var multi = Path.Combine(_paths.Data, "in", "two-pages.tif");
        var images = new OpenCvImageProcessor(NullLogger<OpenCvImageProcessor>.Instance);
        await new LibTiffAssembler(images, _paths, NullLogger<LibTiffAssembler>.Instance).CreateMultiPageTiffAsync(
            [new Page { ImagePath = NoteG4 }, new Page { ImagePath = StapleJpeg }], multi);
        var before = Hash(multi);

        var pages = await ImportAsync(StapleJpeg, multi);

        Assert.Equal(3, pages.Count);
        Assert.Equal([0, 1, 2], pages.Select(p => p.Order));
        Assert.Equal(before, Hash(multi));

        Assert.True(Cv2.ImReadMulti(multi, out var expected, ImreadModes.Unchanged));
        for (var i = 0; i < 2; i++)
        {
            using var want = expected[i];
            using var got = Cv2.ImDecode(File.ReadAllBytes(pages[i + 1].ImagePath), ImreadModes.Unchanged);
            Assert.Equal(want.Size(), got.Size());
            Assert.Equal(0, Cv2.Norm(want, got, NormTypes.L1));
            Assert.Equal(Compression(multi, (short)i), Compression(pages[i + 1].ImagePath, 0));
            Assert.Equal(1, Directories(pages[i + 1].ImagePath));
        }

        Assert.Equal(300, pages[1].Dpi);
    }

    [Fact]
    public async Task Unsupported_files_are_refused()
    {
        var pdf = Path.Combine(_paths.Data, "in", "doc.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllTextAsync(pdf, "%PDF-1.7");

        await Assert.ThrowsAsync<NotSupportedException>(() => ImportAsync(pdf));
    }

    public void Dispose()
    {
        if (Directory.Exists(_paths.Data))
            Directory.Delete(_paths.Data, recursive: true);
    }

    private async Task<List<Page>> ImportAsync(params string[] files)
    {
        var pages = new List<Page>();
        await foreach (var page in Importer.ImportAsync(files))
            pages.Add(page);
        return pages;
    }

    private static Compression Compression(string path, short directory)
    {
        using var tiff = Tiff.Open(path, "r");
        tiff.SetDirectory(directory);
        return (Compression)tiff.GetField(TiffTag.COMPRESSION)[0].ToInt();
    }

    private static int Directories(string path)
    {
        using var tiff = Tiff.Open(path, "r");
        return tiff.NumberOfDirectories();
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
