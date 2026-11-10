using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chango.Conversion;
using Chango.Conversion.Imaging;
using Chango.Core;
using VImage = NetVips.Image;

namespace Chango.Tools;

/// <summary>
/// One-click image tools. Output keeps the source format and is numbered ("photo.jpg" → "photo_1.jpg"), except compression,
/// which may turn an opaque PNG/BMP/TIFF into a JPEG because that is what actually makes it small. Pictures libvips
/// reads are processed by it (fast, and every result keeps the camera details, date taken, location and colour
/// profile); the rest go through Windows' imaging. Several files are worked on at once.
/// </summary>
public sealed class ImageToolsProvider : IOperationProvider, IEstimatesSize
{
    public const string ProviderId = "image.tools";
    public const string EditId = "tool.image.edit";
    public string Id => ProviderId;

    private static readonly OperationGroup CompressGroup = new("image.compress", "Compress", "");
    private static readonly OperationGroup ResizeGroup = new("image.resize", "Resize", "");
    private static readonly OperationGroup RotateGroup = new("image.rotate", "Rotate & flip", "");

    /// <summary>
    /// Compression presets: a fraction of the original size, an absolute ceiling in bytes, or neither ("Optimize": the
    /// same look in fewer bytes, in the same format).
    /// </summary>
    private sealed record CompressPreset(string Key, string Label, string Hint, double? Fraction, long? MaxBytes)
    {
        public bool IsOptimize => Fraction is null && MaxBytes is null;
    }

    private static readonly CompressPreset[] CompressPresets =
    [
        new("optimize", "Optimize", "Smaller file, same look", null, null),
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

    private sealed record Transform(string Key, string Label, string Glyph, bool Mirror, string Progress,
        Func<BitmapSource, BitmapSource> Apply, Func<VImage, VImage> ApplyVips, OperationGroup? Group);

    private static readonly Transform[] Transforms =
    [
        new("rotate-right", "Rotate right", "", false, "Rotating",
            b => WicImageCodec.Transform(b, new RotateTransform(90)), v => v.Rot90(), RotateGroup),
        new("rotate-left", "Rotate left", "", true, "Rotating",
            b => WicImageCodec.Transform(b, new RotateTransform(-90)), v => v.Rot270(), RotateGroup),
        new("flip", "Flip horizontal", "", false, "Flipping",
            b => WicImageCodec.Transform(b, new ScaleTransform(-1, 1)), v => v.FlipHor(), RotateGroup),
        new("flip-vertical", "Flip vertical", "", false, "Flipping",
            b => WicImageCodec.Transform(b, new ScaleTransform(1, -1)), v => v.FlipVer(), RotateGroup),
        new("rotate-180", "Rotate 180°", "", false, "Rotating",
            b => WicImageCodec.Transform(b, new RotateTransform(180)), v => v.Rot180(), RotateGroup),
        new("grayscale", "Grayscale", "", false, "Converting to grayscale",
            WicImageCodec.Grayscale, Grayscale, null),
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
                file => new(file, OutcomeStatus.Succeeded, WicImageCodec.WriteOutput(edited, file, WicImageCodec.OutputFormatFor(file), numbered: true, context, ct)),
            var id when id.StartsWith("tool.image.compress.") =>
                file => Compress(file, CompressPresets.Single(p => id == $"tool.image.compress.{p.Key}"), context, ct),
            var id when id.StartsWith("tool.image.resize.") =>
                file => Resize(file, ResizePresets.Single(p => id == $"tool.image.resize.{p.Key}"), context, ct),
            var id => file => Apply(file, Transforms.Single(t => id == $"tool.image.{t.Key}"), context, ct),
        };
        return StaWorker.ForEachAsync(files, file =>
        {
            try { progress.Report(run(file)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: ImageConversionProvider.Friendly(ex)));
            }
        }, ct, "Chango image tools");
    }

    /// <summary>libvips does the whole job when it reads the file and writes its format.</summary>
    private static bool ByVips(string file) => VipsCodec.CanRead(file) && VipsCodec.CanWrite(WicImageCodec.OutputFormatFor(file));

    /// <summary>Encodes a libvips result in the file's own format and publishes it, numbered.</summary>
    private static string WriteVips(VImage image, string file, OperationContext context, CancellationToken ct)
    {
        var format = WicImageCodec.OutputFormatFor(file);
        int w = image.Width, h = image.Height;
        byte[] data = VipsCodec.Encode(image, format, context.JpegQuality);
        return WicImageCodec.Publish(file, context, format, true, data, temp => VipsCodec.Validate(temp, w, h), ct);
    }

    private static FileOutcome Apply(string file, Transform tool, OperationContext context, CancellationToken ct)
    {
        if (ByVips(file))
        {
            using var image = VipsCodec.Load(file);
            using var result = tool.ApplyVips(image);
            return new(file, OutcomeStatus.Succeeded, WriteVips(result, file, context, ct));
        }
        var bitmap = tool.Apply(WicImageCodec.Load(file));
        return new(file, OutcomeStatus.Succeeded, WicImageCodec.WriteOutput(bitmap, file, WicImageCodec.OutputFormatFor(file), numbered: true, context, ct));
    }

    /// <summary>Luminance in place of colour, back in the picture's own colour space; transparency is kept.</summary>
    private static VImage Grayscale(VImage image)
    {
        bool deep = image.Format == NetVips.Enums.BandFormat.Ushort;
        using var grey = image.Colourspace(deep ? NetVips.Enums.Interpretation.Grey16 : NetVips.Enums.Interpretation.Bw);
        return grey.Colourspace(image.Interpretation);
    }

    private static FileOutcome Resize(string file, ResizePreset preset, OperationContext context, CancellationToken ct)
    {
        var (width, height) = SizeEstimate.Dimensions(file);
        (int W, int H)? size = preset.Scale is { } k
            ? (Math.Max(1, (int)Math.Round(width * k)), Math.Max(1, (int)Math.Round(height * k)))
            : WicImageCodec.FitLongSide(width, height, preset.LongSide!.Value);
        if (size is not { } s)
            return new(file, OutcomeStatus.Skipped, Message: $"Already within {preset.Label} ({width}×{height})");
        if (ByVips(file))
        {
            // Shrink-on-load: a JPEG is decoded straight at ½, ¼ or ⅛ size, then finished with Lanczos.
            using var thumb = VImage.Thumbnail(file, s.W, height: s.H, size: NetVips.Enums.Size.Force);
            return new(file, OutcomeStatus.Succeeded, WriteVips(thumb, file, context, ct));
        }
        var resized = WicImageCodec.Resize(WicImageCodec.Load(file), s.W, s.H);
        return new(file, OutcomeStatus.Succeeded, WicImageCodec.WriteOutput(resized, file, WicImageCodec.OutputFormatFor(file), numbered: true, context, ct));
    }

    private static FileOutcome Compress(string file, CompressPreset preset, OperationContext context, CancellationToken ct)
    {
        long original = new FileInfo(file).Length;
        if (preset.MaxBytes is { } max && original <= max)
            return new(file, OutcomeStatus.Skipped, Message: $"Already under {preset.Label}");
        long? target = preset.IsOptimize ? null : preset.MaxBytes ?? (long)(original * preset.Fraction!.Value);

        (byte[] Data, ImageFormat Format, int Width, int Height) result;
        if (VipsCodec.CanRead(file))
        {
            var r = VipsCompressor.Compress(file, target);
            result = (r.Data, r.Format, r.Width, r.Height);
        }
        else
        {
            // HEIC, RAW, BMP …: decoded by Windows. "Optimize" asks for a good JPEG at about 60% of the size.
            var r = ImageCompressor.Compress(WicImageCodec.Load(file), WicImageCodec.FormatOf(file)!, target ?? (long)(original * 0.6));
            result = (r.Data, r.Format, r.Width, r.Height);
        }
        // Never "compress" into something bigger; "Optimize" must save at least 5% to be worth a second file; for
        // presets with a ceiling, missing it is a failure worth reporting.
        if (result.Data.Length >= original * (preset.IsOptimize ? 0.95 : 1.0))
            return new(file, OutcomeStatus.Skipped, Message: "This image is already as small as it can get");
        if (preset.MaxBytes is { } ceiling && result.Data.Length > ceiling)
            return new(file, OutcomeStatus.Failed, Message: $"Couldn't get it under {preset.Label} without ruining it");

        string output = WicImageCodec.Publish(file, context, result.Format, true, result.Data,
            validate: temp => VipsCodec.Available
                ? VipsCodec.Validate(temp, result.Width, result.Height)
                : WicImageCodec.Load(temp) is var check && check.PixelWidth == result.Width && check.PixelHeight == result.Height
                    ? null : "the compressed image didn't decode correctly", ct);
        return new(file, OutcomeStatus.Succeeded, output);
    }

    /// <summary>Compress: the preset's own target. Resize: the share of pixels kept (never enlarges).</summary>
    public IEnumerable<(string OperationId, long Bytes)> EstimateSizes(string file, OperationContext context)
    {
        if (!WicImageCodec.IsSupportedInput(file)) yield break;
        long size = new FileInfo(file).Length;
        foreach (var p in CompressPresets.Where(p => !p.IsOptimize)) // Optimize's saving can't be known without doing it
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
        yield return ("Image tools", "Compress (optimize, 50/75/25%, under 1 MB/500 KB/200 KB), resize (50%, 1920/1280/800 px), " +
            string.Join(", ", Transforms.Select(t => t.Label.ToLowerInvariant())));
    }
}
