using System.Diagnostics;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;
using IProfiler.Infrastructure.Imaging;
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

    // Orientation: the largest text boxes are read both ways round, each cut to at most this many line
    // heights long (recognition cost grows with width); 180 deg must win by the margin.
    private const int OrientationSampleLines = 8;
    private const int OrientationMaxAspect = 8;
    private const double OrientationMargin = 0.10;

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

    /// <summary>
    /// Upright vs upside down: text detection once (box positions don't depend on the reading direction),
    /// then the same line crops are recognised as they are and turned 180 degrees. Upside-down text reads
    /// as low-confidence garbage, so the direction with the clearly higher mean confidence wins. Much
    /// cheaper than two full-page OCR passes.
    /// </summary>
    public Task<PageOrientation> DetectOrientationAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => DetectOrientation(imagePath, cancellationToken), cancellationToken);

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
        using var page = ImageFile.Load(imagePath, ImreadModes.Color);

        var area = ImageFile.Clip(region ?? new PixelRect(0, 0, page.Width, page.Height), page.Width, page.Height);
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

    private PageOrientation DetectOrientation(string imagePath, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var page = ImageFile.Load(imagePath, ImreadModes.Color);
        using var input = Prepare(page, new BoxMap(new PixelRect(0, 0, page.Width, page.Height), 0));
        cancellationToken.ThrowIfCancellationRequested();

        var upright = new List<Mat>();
        var rotated = new List<Mat>();
        try
        {
            PaddleOcrRecognizerResult[] asIs, turned;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var boxes = Engine.Detector.Run(input)
                    .Where(b => Math.Min(b.Size.Width, b.Size.Height) >= 8)
                    .OrderByDescending(b => b.Size.Width * b.Size.Height)
                    .Take(OrientationSampleLines);
                foreach (var box in boxes)
                {
                    using var line = PaddleOcrAll.GetRotateCropImage(input, box);
                    var crop = line.Width > OrientationMaxAspect * line.Height
                        ? new Mat(line, new Rect(0, 0, OrientationMaxAspect * line.Height, line.Height)).Clone()
                        : line.Clone();
                    upright.Add(crop);
                    var flipped = new Mat();
                    Cv2.Rotate(crop, flipped, RotateFlags.Rotate180);
                    rotated.Add(flipped);
                }

                if (upright.Count == 0)
                {
                    logger.LogInformation("Orientation {File}: no text found, left as it is", Path.GetFileName(imagePath));
                    return PageOrientation.Unknown;
                }

                asIs = Engine.Recognizer.Run([.. upright]);
                turned = Engine.Recognizer.Run([.. rotated]);
            }

            var uprightScore = MeanConfidence(asIs);
            var rotatedScore = MeanConfidence(turned);
            var rotation = rotatedScore > uprightScore + OrientationMargin ? PageRotation.Rotate180 : PageRotation.None;
            logger.LogInformation(
                "Orientation {File}: {Rotation} (upright {Upright:F2} vs 180 deg {Rotated:F2} over {Lines} lines) in {ElapsedMs} ms",
                Path.GetFileName(imagePath), rotation, uprightScore, rotatedScore, upright.Count, sw.ElapsedMilliseconds);
            return new PageOrientation(rotation, uprightScore, rotatedScore, upright.Count);
        }
        finally
        {
            foreach (var m in upright.Concat(rotated))
                m.Dispose();
        }

        static double MeanConfidence(PaddleOcrRecognizerResult[] results) =>
            results.Average(r => string.IsNullOrWhiteSpace(r.Text) ? 0.0 : r.Score);
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

    /// <summary>Crops the area (with a white margin for regions) and downscales it to <see cref="MaxLongSide"/>.</summary>
    private static Mat Prepare(Mat page, BoxMap map)
    {
        using var padded = ImageFile.CropWithMargin(page, map.Area, map.Padding);

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
