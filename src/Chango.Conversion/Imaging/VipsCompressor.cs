using VImage = NetVips.Image;

namespace Chango.Conversion.Imaging;

/// <summary>
/// Compression on libvips, keeping all metadata. Two jobs:
/// <list type="bullet">
/// <item><b>Optimize</b> (no target): the same look in fewer bytes, in the same format. JPEG is re-encoded with mozjpeg,
/// PNG becomes a dithered 256-colour palette when that looks the same, WebP is re-encoded.</item>
/// <item><b>A byte budget</b>: the highest quality that fits. The quality is searched with the quick encoder, then the
/// kept file is encoded once with mozjpeg, which at the same setting is about a fifth smaller, so it still fits with
/// a little more quality. Only when even the lowest acceptable quality is too big is the picture scaled down.</item>
/// </list>
/// Photos (no transparency) become JPEG, as before; transparent pictures stay PNG (palette first, then scaled), and
/// WebP stays WebP.
/// </summary>
public static class VipsCompressor
{
    public const int MinQuality = 40, MaxQuality = 90, OptimizeQuality = 82;
    private const int MaxRounds = 6, MinSide = 32;

    public sealed record Result(byte[] Data, ImageFormat Format, int Width, int Height, int? Quality);

    public static Result Compress(string path, long? targetBytes)
    {
        var source = WicImageCodec.FormatOf(path) ?? throw new NotSupportedException(Path.GetExtension(path));
        var image = VipsCodec.Load(path, inMemory: true);
        try
        {
            bool alpha = VipsCodec.HasTransparency(image);
            if (source == WicImageCodec.Webp) return Search(image, WicImageCodec.Webp, targetBytes, VipsCodec.Webp);
            if (alpha) return Transparent(image, targetBytes);
            if (source == WicImageCodec.Png && targetBytes is null) return Optimized(image, WicImageCodec.Png, VipsCodec.PalettePng(image, 90));
            if (source == WicImageCodec.Png && PaletteFits(image, targetBytes!.Value) is { } palette) return palette;

            var jpeg = source == WicImageCodec.Jfif ? WicImageCodec.Jfif : WicImageCodec.Jpg;
            var flat = VipsCodec.FlattenOnWhite(image);
            if (!ReferenceEquals(flat, image)) { image.Dispose(); image = flat; }
            return Jpeg(image, jpeg, targetBytes);
        }
        finally { image.Dispose(); }
    }

    private static Result Optimized(VImage image, ImageFormat format, byte[] data) =>
        new(data, format, image.Width, image.Height, null);

    /// <summary>JPEG: optimize at a fixed visually-lossless quality, or search for the budget.</summary>
    private static Result Jpeg(VImage image, ImageFormat format, long? targetBytes)
    {
        if (targetBytes is null)
            return new(VipsCodec.Jpeg(image, OptimizeQuality, smallest: true), format, image.Width, image.Height, OptimizeQuality);

        long target = targetBytes.Value;
        var current = image;
        Result? best = null;
        try
        {
            for (int round = 0; round < MaxRounds; round++)
            {
                // The kept file is encoded once with mozjpeg at the predicted quality; only if the prediction was
                // optimistic does it step down.
                for (int q = PredictJpegQuality(current, target) ?? -1; q >= MinQuality; q -= 3)
                {
                    byte[] kept = VipsCodec.Jpeg(current, q, smallest: true);
                    if (kept.Length <= target) return new(kept, format, current.Width, current.Height, q);
                    if (best is null || kept.Length < best.Data.Length) best = new(kept, format, current.Width, current.Height, q);
                }
                // Not even the lowest acceptable quality fits: a smaller picture.
                byte[] lowest = VipsCodec.Jpeg(current, MinQuality, smallest: true);
                var attempt = new Result(lowest, format, current.Width, current.Height, MinQuality);
                if (lowest.Length <= target) return attempt;
                if (best is null || attempt.Data.Length < best.Data.Length) best = attempt;
                if (Smaller(ref current, image, target, lowest.Length) is false) break;
            }
            return best!; // closest we could get; the caller decides whether it's good enough
        }
        finally { if (!ReferenceEquals(current, image)) current.Dispose(); }
    }

    /// <summary>WebP stays WebP: same search, libwebp's own encoder.</summary>
    private static Result Search(VImage image, ImageFormat format, long? targetBytes, Func<VImage, int, byte[]> encode)
    {
        if (targetBytes is null)
            return new(encode(image, OptimizeQuality - 2), format, image.Width, image.Height, OptimizeQuality - 2);
        long target = targetBytes.Value;
        var current = image;
        Result? best = null;
        try
        {
            for (int round = 0; round < MaxRounds; round++)
            {
                var (quality, data) = HighestFitting(q => encode(current, q), target);
                var attempt = new Result(data, format, current.Width, current.Height, quality ?? MinQuality);
                if (quality is not null) return attempt;
                if (best is null || attempt.Data.Length < best.Data.Length) best = attempt;
                if (Smaller(ref current, image, target, data.Length) is false) break;
            }
            return best!;
        }
        finally { if (!ReferenceEquals(current, image)) current.Dispose(); }
    }

    /// <summary>Transparent pictures stay PNG: a palette at falling quality, then smaller.</summary>
    private static Result Transparent(VImage image, long? targetBytes)
    {
        if (targetBytes is null) return Optimized(image, WicImageCodec.Png, VipsCodec.PalettePng(image, 90));
        long target = targetBytes.Value;
        var current = image;
        Result? best = null;
        try
        {
            for (int round = 0; round < MaxRounds; round++)
            {
                foreach (int quality in new[] { 90, 70, 50 })
                {
                    var data = VipsCodec.PalettePng(current, quality);
                    var attempt = new Result(data, WicImageCodec.Png, current.Width, current.Height, quality);
                    if (data.Length <= target) return attempt;
                    if (best is null || data.Length < best.Data.Length) best = attempt;
                }
                if (Smaller(ref current, image, target, best!.Data.Length) is false) break;
            }
            return best!;
        }
        finally { if (!ReferenceEquals(current, image)) current.Dispose(); }
    }

    /// <summary>
    /// An opaque PNG that fits the budget as a palette PNG stays a crisp PNG instead of becoming a JPEG. Tried only
    /// when a quarter-size sample says it's likely, because quantising a large photo is slow and rarely wins.
    /// </summary>
    private static Result? PaletteFits(VImage image, long target)
    {
        using var sample = image.Resize(0.5, kernel: NetVips.Enums.Kernel.Linear);
        double predicted = VipsCodec.PalettePng(sample, 80).Length * Math.Pow(4, 0.9);
        if (predicted > target * 0.9) return null;
        var data = VipsCodec.PalettePng(image, 80);
        return data.Length <= target ? new(data, WicImageCodec.Png, image.Width, image.Height, 80) : null;
    }

    /// <summary>
    /// The highest JPEG quality whose mozjpeg file is predicted to fit <paramref name="target"/>, or null when even
    /// <see cref="MinQuality"/> won't. Searched on a half-size sample with the quick encoder (about a tenth of the
    /// work), then scaled by two measured ratios: full size to sample, and mozjpeg to the quick encoder. On 4K photos
    /// this needs about two full-size encodes in all instead of eight.
    /// </summary>
    private static int? PredictJpegQuality(VImage image, long target)
    {
        const int probe = 75;
        using var sample = image.Width * (long)image.Height > 2_000_000 ? image.Resize(0.5, kernel: NetVips.Enums.Kernel.Linear) : image.Copy();
        using var held = sample.CopyMemory();
        long Quick(VImage v, int q) => VipsCodec.Jpeg(v, q, smallest: false).Length;
        long sampleQuick = Quick(held, probe);
        double toFull = image.Width == held.Width ? 1.0 : (double)Quick(image, probe) / sampleQuick;
        double toBest = (double)VipsCodec.Jpeg(held, probe, smallest: true).Length / sampleQuick;
        long budget = (long)(target * 0.97); // a little margin: predictions are close, not exact
        long Predicted(int q) => (long)(Quick(held, q) * toFull * toBest);
        int lo = MinQuality, hi = MaxQuality;
        if (Predicted(lo) > budget) return null;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Predicted(mid) <= budget) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// Highest quality in [<see cref="MinQuality"/>, <see cref="MaxQuality"/>] whose encoding fits, with its bytes;
    /// (null, the minimum's bytes) when none does. At most seven encodes.
    /// </summary>
    private static (int? Quality, byte[] Data) HighestFitting(Func<int, byte[]> encode, long target)
    {
        int lo = MinQuality, hi = MaxQuality;
        byte[] fit = encode(lo);
        if (fit.Length > target) return (null, fit);
        int fitQ = lo;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            var data = encode(mid);
            if (data.Length <= target) { lo = mid; fit = data; fitQ = mid; }
            else hi = mid - 1;
        }
        return (fitQ, fit);
    }

    /// <summary>
    /// Scales <paramref name="current"/> down by the square root of the overshoot (area ∝ bytes), with margin. False
    /// when it would become too small to be worth having.
    /// </summary>
    private static bool Smaller(ref VImage current, VImage original, long target, long size)
    {
        double k = Math.Sqrt((double)target / size) * 0.92;
        if (Math.Min(current.Width * k, current.Height * k) < MinSide) return false;
        var next = current.Resize(k, kernel: NetVips.Enums.Kernel.Lanczos3);
        if (!ReferenceEquals(current, original)) current.Dispose();
        current = next.CopyMemory();
        next.Dispose();
        return true;
    }
}
