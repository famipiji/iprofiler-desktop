using System.Diagnostics;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;

namespace IProfiler.Infrastructure.Ocr;

/// <summary>
/// PP-OCRv5 mobile (Sdcb.PaddleOCR) with the models read from <see cref="AppPaths.Models"/> only:
/// nothing is downloaded at runtime. One engine for the whole app, loaded on first use and called one
/// request at a time (it is not thread-safe and holds hundreds of MB). Register as a singleton.
/// </summary>
public sealed class PaddleOcrProvider(AppPaths paths, ILogger<PaddleOcrProvider> logger) : IOcrProvider, IDisposable
{
    /// <summary>Pages are downscaled to this longest side before OCR (about 3x faster, no loss of useful text).</summary>
    public const int MaxLongSide = 1600;

    /// <summary>Lines below this confidence are dropped (removes most faint watermark text).</summary>
    public const double MinLineConfidence = 0.5;

    // White margin around a region crop: the detector misses text that touches the image edge.
    private const int RegionPadding = 16;

    private readonly Lock _gate = new();
    private PaddleOcrAll? _engine;
    private bool _disposed;

    public string DetectionModelDirectory => Path.Combine(paths.Models, "ppocrv5-mobile", "det");

    public string RecognitionModelDirectory => Path.Combine(paths.Models, "ppocrv5-mobile", "rec-en");

    public Task<OcrResult> RecognizePageAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Recognize(imagePath, region: null, cancellationToken), cancellationToken);

    /// <summary>OCR of one rectangle of the page ("FlyOCR"). Boxes come back in page pixels.</summary>
    public Task<OcrResult> RecognizeRegionAsync(string imagePath, PixelRect region, CancellationToken cancellationToken = default) =>
        Task.Run(() => Recognize(imagePath, region, cancellationToken), cancellationToken);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _engine?.Dispose();
            _engine = null;
        }
    }

    private OcrResult Recognize(string imagePath, PixelRect? region, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        using var page = Load(imagePath);

        var area = Clip(region ?? new PixelRect(0, 0, page.Width, page.Height), page.Width, page.Height);
        if (area.Width == 0 || area.Height == 0)
        {
            logger.LogWarning("OCR region {Region} lies outside {File} ({Width}x{Height}); nothing to read",
                region, Path.GetFileName(imagePath), page.Width, page.Height);
            return OcrResult.Empty;
        }

        var map = new BoxMap(area, region is null ? 0 : RegionPadding);
        using var input = Prepare(page, map);

        cancellationToken.ThrowIfCancellationRequested();
        PaddleOcrResult raw;
        var inference = Stopwatch.StartNew();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            raw = Engine.Run(input);
        }

        inference.Stop();

        var kept = raw.Regions
            .Where(r => r.Score >= MinLineConfidence && !string.IsNullOrWhiteSpace(r.Text))
            // In a region, text cut off by the region's edge shows up as a fragment in the white margin.
            .Where(r => map.CenterIsInside(r.Rect.Center))
            .Select(r => ToLine(r, input, map))
            .ToList();
        var lines = ReadingOrder(kept);

        logger.LogInformation(
            "OCR {Kind} {File}: {Width}x{Height} px read at {InputWidth}x{InputHeight}, {Lines} lines kept of {Found} " +
            "(confidence >= {MinConfidence}), inference {InferenceMs} ms, total {TotalMs} ms",
            region is null ? "page" : "region", Path.GetFileName(imagePath), area.Width, area.Height,
            input.Width, input.Height, lines.Count, raw.Regions.Length, MinLineConfidence,
            inference.ElapsedMilliseconds, total.ElapsedMilliseconds);

        return new OcrResult(lines);
    }

    /// <summary>Created inside the lock on first use, so the first call pays the model-loading cost.</summary>
    private PaddleOcrAll Engine
    {
        get
        {
            if (_engine is not null)
                return _engine;

            var load = Stopwatch.StartNew();
            var model = new FullOcrModel(
                DetectionModel.FromDirectory(DetectionModelDirectory, ModelVersion.V5),
                RecognizationModel.FromDirectoryV5(RecognitionModelDirectory));

            _engine = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = true,
                Enable180Classification = false,
            };
            // We downscale ourselves; stop the detector from shrinking the input a second time.
            _engine.Detector.MaxSize = MaxLongSide;

            logger.LogInformation("PaddleOCR engine loaded from {Models} in {ElapsedMs} ms",
                paths.Models, load.ElapsedMilliseconds);
            return _engine;
        }
    }

    private static Mat Load(string imagePath)
    {
        // ImDecode instead of ImRead: ImRead can't open non-ASCII paths on Windows.
        var image = Cv2.ImDecode(File.ReadAllBytes(imagePath), ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidDataException($"Not an image OpenCV can read: {imagePath}");
        }

        return image;
    }

    /// <summary>Crops the area (with a white margin for regions) and downscales it to <see cref="MaxLongSide"/>.</summary>
    private static Mat Prepare(Mat page, BoxMap map)
    {
        var area = map.Area;
        var pad = map.Padding;
        using var crop = new Mat(page, new Rect(area.X, area.Y, area.Width, area.Height));
        using var padded = new Mat();
        Cv2.CopyMakeBorder(crop, padded, pad, pad, pad, pad, BorderTypes.Constant, Scalar.White);

        map.Scale = Math.Min(1.0, (double)MaxLongSide / Math.Max(padded.Width, padded.Height));
        var result = new Mat();
        if (map.Scale < 1.0)
            Cv2.Resize(padded, result, new Size(), map.Scale, map.Scale, InterpolationFlags.Area);
        else
            padded.CopyTo(result);

        return result;
    }

    private OcrLine ToLine(PaddleOcrResultRegion region, Mat input, BoxMap map)
    {
        var box = region.Rect.BoundingRect() & new Rect(0, 0, input.Width, input.Height);
        var words = WordSpans(region.Text, input, box)
            .Select(w => new OcrWord(w.Text, map.ToPage(new Rect(w.Left, box.Y, w.Right - w.Left, box.Height))))
            .ToList();
        return new OcrLine(region.Text, map.ToPage(box), region.Score, words);
    }

    /// <summary>
    /// PaddleOCR returns lines, and Sdcb.PaddleOCR exposes no per-character positions. Word boxes come from
    /// the line's own pixels instead: the n-1 widest ink-free column gaps are the n-1 spaces. Splitting by
    /// character count misplaces words when big and small text share a line (Flask migration lesson).
    /// </summary>
    private List<(string Text, int Left, int Right)> WordSpans(string text, Mat input, Rect box)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1 || box.Width < 2 || box.Height < 2)
            return [(text.Trim(), box.Left, box.Right)];

        using var line = new Mat(input, box);
        using var gray = new Mat();
        Cv2.CvtColor(line, gray, ColorConversionCodes.BGR2GRAY);
        using var ink = new Mat();
        Cv2.Threshold(gray, ink, 0, 1, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        using var columns = new Mat();
        Cv2.Reduce(ink, columns, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);

        var hasInk = new bool[box.Width];
        for (var x = 0; x < box.Width; x++)
            hasInk[x] = columns.At<int>(0, x) > 0;

        var first = Array.IndexOf(hasInk, true);
        var last = Array.LastIndexOf(hasInk, true);
        var gaps = new List<(int Start, int End)>();
        for (var x = first; first >= 0 && x <= last; x++)
        {
            if (hasInk[x])
                continue;
            var start = x;
            while (x <= last && !hasInk[x])
                x++;
            gaps.Add((start, x));
        }

        if (gaps.Count < words.Length - 1)
        {
            logger.LogDebug("Line \"{Text}\": {Gaps} gaps for {Words} words; using one box for the line",
                text, gaps.Count, words.Length);
            return [(text.Trim(), box.Left, box.Right)];
        }

        var spaces = gaps.OrderByDescending(g => g.End - g.Start).Take(words.Length - 1).OrderBy(g => g.Start).ToList();
        var spans = new List<(string, int, int)>(words.Length);
        var left = first;
        for (var i = 0; i < words.Length; i++)
        {
            var right = i < spaces.Count ? spaces[i].Start : last + 1;
            spans.Add((words[i], box.Left + left, box.Left + right));
            if (i < spaces.Count)
                left = spaces[i].End;
        }

        return spans;
    }

    /// <summary>
    /// Top-to-bottom rows, left-to-right within a row. Sorting by Y alone breaks on slightly skewed
    /// scans, where the right end of a row sits a few pixels higher than its left end.
    /// </summary>
    private static List<OcrLine> ReadingOrder(List<OcrLine> lines)
    {
        var rows = new List<List<OcrLine>>();
        foreach (var line in lines.OrderBy(l => CenterY(l.Box)))
        {
            var row = rows.LastOrDefault();
            if (row is not null && Math.Abs(CenterY(line.Box) - row.Average(l => CenterY(l.Box)))
                <= Math.Min(line.Box.Height, row.Min(l => l.Box.Height)) / 2.0)
                row.Add(line);
            else
                rows.Add([line]);
        }

        return rows.SelectMany(r => r.OrderBy(l => l.Box.X)).ToList();

        static double CenterY(PixelRect b) => b.Y + b.Height / 2.0;
    }

    private static PixelRect Clip(PixelRect r, int width, int height)
    {
        var left = Math.Clamp(r.X, 0, width);
        var top = Math.Clamp(r.Y, 0, height);
        var right = Math.Clamp(r.X + r.Width, left, width);
        var bottom = Math.Clamp(r.Y + r.Height, top, height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>Maps boxes from the prepared (padded, downscaled) image back to original page pixels.</summary>
    private sealed class BoxMap(PixelRect area, int padding)
    {
        public PixelRect Area => area;

        public int Padding => padding;

        public double Scale { get; set; } = 1.0;

        public bool CenterIsInside(Point2f p)
        {
            var x = p.X / Scale - padding;
            var y = p.Y / Scale - padding;
            return x >= 0 && y >= 0 && x < area.Width && y < area.Height;
        }

        public PixelRect ToPage(Rect r)
        {
            var left = (int)Math.Floor(r.Left / Scale) - padding + area.X;
            var top = (int)Math.Floor(r.Top / Scale) - padding + area.Y;
            var right = (int)Math.Ceiling(r.Right / Scale) - padding + area.X;
            var bottom = (int)Math.Ceiling(r.Bottom / Scale) - padding + area.Y;

            left = Math.Clamp(left, area.X, area.X + area.Width);
            top = Math.Clamp(top, area.Y, area.Y + area.Height);
            right = Math.Clamp(right, left, area.X + area.Width);
            bottom = Math.Clamp(bottom, top, area.Y + area.Height);
            return new PixelRect(left, top, right - left, bottom - top);
        }
    }
}
