using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Conversion;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Core;

namespace OrbitDrop.Tools;

/// <summary>
/// One-click image tools. Output keeps the source format with a suffix ("photo (rotated).jpg"), except compression,
/// which may turn an opaque PNG/BMP/TIFF into a JPEG because that is what actually makes it small.
/// </summary>
public sealed class ImageToolsProvider : IOperationProvider, IEstimatesSize
{
    public const string ProviderId = "image.tools";
    public const string EditId = "tool.image.edit";
    public string Id => ProviderId;

    private static readonly OperationGroup CompressGroup = new("image.compress", "Compress", "");
    private static readonly OperationGroup ResizeGroup = new("image.resize", "Resize", "");
    private static readonly OperationGroup RotateGroup = new("image.rotate", "Rotate & flip", "");

    /// <summary>Compression presets: a fraction of the original size, or an absolute ceiling in bytes.</summary>
    private sealed record CompressPreset(string Key, string Label, string Hint, double? Fraction, long? MaxBytes);

    private static readonly CompressPreset[] CompressPresets =
    [
        new("half", "50%", "Compress to about half the size", 0.5, null),
        new("p75", "75%", "Compress to about 75% of the size", 0.75, null),
        new("p25", "25%", "Compress to about a quarter of the size", 0.25, null),
        new("1mb", "1 MB", "Compress to under 1 MB", null, 1_000_000),
        new("500kb", "500 KB", "Compress to under 500 KB", null, 500_000),
        new("200kb", "200 KB", "Compress to under 200 KB", null, 200_000),
    ];

    /// <summary>Resize presets: a scale factor, or a maximum length for the longer side.</summary>
    private sealed record ResizePreset(string Key, string Label, string Hint, double? Scale, int? LongSide);

    private static readonly ResizePreset[] ResizePresets =
    [
        new("half", "50%", "Resize to half the width and height", 0.5, null),
        new("1920", "1920 px", "Fit within 1920 px", null, 1920),
        new("1280", "1280 px", "Fit within 1280 px", null, 1280),
        new("800", "800 px", "Fit within 800 px", null, 800),
    ];

    private sealed record Transform(string Key, string Label, string Glyph, bool Mirror, string Progress, string Suffix,
        Func<BitmapSource, BitmapSource> Apply, OperationGroup? Group);

    private static readonly Transform[] Transforms =
    [
        new("rotate-right", "Rotate right", "", false, "Rotating", "rotated",
            b => WicImageCodec.Transform(b, new RotateTransform(90)), RotateGroup),
        new("rotate-left", "Rotate left", "", true, "Rotating", "rotated",
            b => WicImageCodec.Transform(b, new RotateTransform(-90)), RotateGroup),
        new("flip", "Flip horizontal", "", false, "Flipping", "flipped",
            b => WicImageCodec.Transform(b, new ScaleTransform(-1, 1)), RotateGroup),
        new("flip-vertical", "Flip vertical", "", false, "Flipping", "flipped",
            b => WicImageCodec.Transform(b, new ScaleTransform(1, -1)), RotateGroup),
        new("rotate-180", "Rotate 180°", "", false, "Rotating", "rotated",
            b => WicImageCodec.Transform(b, new RotateTransform(180)), RotateGroup),
        new("grayscale", "Grayscale", "", false, "Converting to grayscale", "grayscale",
            WicImageCodec.Grayscale, null),
    ];

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Tools || !files.All(WicImageCodec.IsSupportedInput)) yield break;

        if (files.Count == 1)
            yield return new(EditId, ProviderId, OperationMode.Tools, "Edit", "\uE70F", "Crop, adjust, mark up or redact", "",
                "Saving edits", 90, NeedsInput: true);
        int order = 100;
        foreach (var p in CompressPresets)
            yield return new($"tool.image.compress.{p.Key}", ProviderId, OperationMode.Tools, p.Label, null,
                p.Hint, "", "Compressing", order++, Group: CompressGroup);
        order = 110;
        foreach (var p in ResizePresets)
            yield return new($"tool.image.resize.{p.Key}", ProviderId, OperationMode.Tools, p.Label, null,
                p.Hint, "", "Resizing", order++, Group: ResizeGroup);
        order = 120;
        foreach (var t in Transforms)
            yield return new($"tool.image.{t.Key}", ProviderId, OperationMode.Tools, t.Label, t.Glyph,
                "%1", t.Label, t.Progress, t.Group is null ? 140 : order++, t.Mirror, t.Group);
    }

    public Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        Func<string, FileOutcome> run = operation.Id switch
        {
            EditId when context.Input is BitmapSource edited =>
                file => new(file, OutcomeStatus.Succeeded, WicImageCodec.WriteOutput(edited, file, WicImageCodec.OutputFormatFor(file), "edited", context, ct)),
            var id when id.StartsWith("tool.image.compress.") =>
                file => Compress(file, CompressPresets.Single(p => id == $"tool.image.compress.{p.Key}"), context, ct),
            var id when id.StartsWith("tool.image.resize.") =>
                file => Resize(file, ResizePresets.Single(p => id == $"tool.image.resize.{p.Key}"), context, ct),
            var id => file => Apply(file, Transforms.Single(t => id == $"tool.image.{t.Key}"), context, ct),
        };
        return StaWorker.RunAsync(() =>
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try { progress.Report(run(file)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: ImageConversionProvider.Friendly(ex)));
                }
            }
        }, "OrbitDrop image tools");
    }

    private static FileOutcome Apply(string file, Transform tool, OperationContext context, CancellationToken ct)
    {
        var result = tool.Apply(WicImageCodec.Load(file));
        return new(file, OutcomeStatus.Succeeded, WicImageCodec.WriteOutput(result, file, WicImageCodec.OutputFormatFor(file), tool.Suffix, context, ct));
    }

    private static FileOutcome Resize(string file, ResizePreset preset, OperationContext context, CancellationToken ct)
    {
        var image = WicImageCodec.Load(file);
        (int W, int H)? size = preset.Scale is { } k
            ? (Math.Max(1, (int)Math.Round(image.PixelWidth * k)), Math.Max(1, (int)Math.Round(image.PixelHeight * k)))
            : WicImageCodec.FitLongSide(image.PixelWidth, image.PixelHeight, preset.LongSide!.Value);
        if (size is not { } s)
            return new(file, OutcomeStatus.Skipped, Message: $"Already within {preset.Label} ({image.PixelWidth}×{image.PixelHeight})");
        var resized = WicImageCodec.Resize(image, s.W, s.H);
        return new(file, OutcomeStatus.Succeeded, WicImageCodec.WriteOutput(resized, file, WicImageCodec.OutputFormatFor(file), "resized", context, ct));
    }

    private static FileOutcome Compress(string file, CompressPreset preset, OperationContext context, CancellationToken ct)
    {
        long original = new FileInfo(file).Length;
        long target = preset.MaxBytes ?? (long)(original * preset.Fraction!.Value);
        if (preset.MaxBytes is { } max && original <= max)
            return new(file, OutcomeStatus.Skipped, Message: $"Already under {preset.Label}");

        var result = ImageCompressor.Compress(WicImageCodec.Load(file), WicImageCodec.FormatOf(file)!, target);
        // Never "compress" into something bigger; for presets with a ceiling, missing it is a failure worth reporting.
        if (result.Data.Length >= original)
            return new(file, OutcomeStatus.Skipped, Message: "This image is already as small as it can get");
        if (preset.MaxBytes is { } ceiling && result.Data.Length > ceiling)
            return new(file, OutcomeStatus.Failed, Message: $"Couldn't get it under {preset.Label} without ruining it");

        string output = SafeOutput.Write(file, context.Output, result.Format.Extension, "compressed",
            write: s => s.Write(result.Data),
            validate: temp =>
            {
                var check = WicImageCodec.Load(temp);
                return check.PixelWidth == result.Width && check.PixelHeight == result.Height ? null : "the compressed image didn't decode correctly";
            }, ct);
        return new(file, OutcomeStatus.Succeeded, output);
    }

    /// <summary>Compress: the preset's own target. Resize: the share of pixels kept (never enlarges).</summary>
    public IEnumerable<(string OperationId, long Bytes)> EstimateSizes(string file, OperationContext context)
    {
        if (!WicImageCodec.IsSupportedInput(file)) yield break;
        long size = new FileInfo(file).Length;
        foreach (var p in CompressPresets)
            yield return ($"tool.image.compress.{p.Key}", p.Fraction is { } f ? (long)(size * f) : Math.Min(size, p.MaxBytes ?? size));
        var (w, h) = SizeEstimate.Dimensions(file);
        if (w <= 0 || h <= 0) yield break;
        foreach (var p in ResizePresets)
        {
            double scale = p.Scale ?? Math.Min(1.0, (double)p.LongSide!.Value / Math.Max(w, h));
            yield return ($"tool.image.resize.{p.Key}", SizeEstimate.Resized(size, scale * scale));
        }
    }

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Image tools", "Compress (50/75/25%, under 1 MB/500 KB/200 KB), resize (50%, 1920/1280/800 px), " +
            string.Join(", ", Transforms.Select(t => t.Label.ToLowerInvariant())));
    }
}
