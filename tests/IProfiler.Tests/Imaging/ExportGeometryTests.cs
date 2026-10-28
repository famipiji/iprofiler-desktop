using IProfiler.Core.Domain;
using IProfiler.Core.Imaging;
using IProfiler.Infrastructure.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace IProfiler.Tests.Imaging;

/// <summary>ExportGeometry must land on the same pixels as the OpenCV export render, both ways round.</summary>
public sealed class ExportGeometryTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "iProfiler-tests", Guid.NewGuid().ToString("N"));

    public static TheoryData<PageRotation, double> Cases()
    {
        var data = new TheoryData<PageRotation, double>();
        foreach (var rotation in Enum.GetValues<PageRotation>())
        {
            data.Add(rotation, 0);
            data.Add(rotation, 3.0);
            data.Add(rotation, -1.7);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Mapping_to_the_export_and_back_is_the_identity(PageRotation rotation, double deskew)
    {
        var page = new Page { ImagePath = "page.tif", Rotation = rotation, DeskewAngle = deskew, Crop = new PixelRect(50, 30, 900, 700) };
        var geometry = new ExportGeometry(page, 1000, 800);

        foreach (var (x, y) in new[] { (50.0, 30.0), (640.0, 220.0), (949.0, 729.0), (123.4, 567.8) })
        {
            var (ex, ey) = geometry.ToExport(x, y);
            var (ox, oy) = geometry.ToOriginal(ex, ey);
            Assert.Equal(x, ox, 6);
            Assert.Equal(y, oy, 6);
        }

        var box = new PixelRect(600, 200, 80, 40);
        Assert.Equal(box, geometry.ToOriginal(geometry.ToExport(box)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Mapped_box_matches_where_the_renderer_puts_the_pixels(PageRotation rotation, double deskew)
    {
        // A white page with one black block; after export the block must be where the geometry says.
        var source = Path.Combine(_out, "block.png");
        Directory.CreateDirectory(_out);
        using (var image = new Mat(800, 1000, MatType.CV_8UC1, Scalar.All(255)))
        {
            image.Rectangle(new Rect(600, 200, 80, 40), Scalar.All(0), -1);
            image.SaveImage(source);
        }

        var page = new Page { ImagePath = source, Rotation = rotation, DeskewAngle = deskew, Crop = new PixelRect(50, 30, 900, 700) };
        var output = Path.Combine(_out, $"export-{rotation}-{deskew}.png");

        var geometry = await new OpenCvImageProcessor(NullLogger<OpenCvImageProcessor>.Instance).RenderForExportAsync(page, output);

        using var exported = Cv2.ImRead(output, ImreadModes.Grayscale);
        Assert.Equal((geometry.Width, geometry.Height), (exported.Width, exported.Height));
        using var ink = new Mat();
        Cv2.Threshold(exported, ink, 127, 255, ThresholdTypes.BinaryInv);
        var found = Cv2.BoundingRect(ink);
        var expected = geometry.ToExport(new PixelRect(600, 200, 80, 40));
        // Centres agree to a pixel; the rendered block's bounding box is a little larger when it is turned by the deskew.
        Assert.InRange(found.X + found.Width / 2.0 - (expected.X + expected.Width / 2.0), -1.0, 1.0);
        Assert.InRange(found.Y + found.Height / 2.0 - (expected.Y + expected.Height / 2.0), -1.0, 1.0);
        Assert.InRange(found.Width - expected.Width, -1, 6);
        Assert.InRange(found.Height - expected.Height, -1, 6);
    }

    [Fact]
    public void Crop_outside_the_image_is_rejected()
    {
        var page = new Page { ImagePath = "page.tif", Crop = new PixelRect(2000, 0, 100, 100) };

        Assert.Throws<InvalidOperationException>(() => new ExportGeometry(page, 1000, 800));
    }

    public void Dispose()
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, recursive: true);
    }
}
