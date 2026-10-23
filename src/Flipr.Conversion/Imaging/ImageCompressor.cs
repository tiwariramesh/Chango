using System.Windows.Media.Imaging;

namespace Flipr.Conversion.Imaging;

/// <summary>
/// Shrinks an image to a byte budget. Photos (no transparency) become JPEG at the highest quality that fits; images
/// with transparency stay PNG and are scaled down instead. If even the lowest acceptable quality is too big, the
/// image is scaled down too. Pure CPU work on an STA thread; no files are touched here.
/// </summary>
public static class ImageCompressor
{
    public const int MinQuality = 40, MaxQuality = 90;
    private const int MaxRounds = 8, MinSide = 32;

    public sealed record Result(byte[] Data, ImageFormat Format, int Width, int Height, int? Quality);

    public static Result Compress(BitmapSource image, ImageFormat sourceFormat, long targetBytes)
    {
        bool alpha = sourceFormat.SupportsAlpha && WicImageCodec.HasTransparency(image);
        var current = image;
        Result? best = null;
        for (int round = 0; round < MaxRounds; round++)
        {
            Result attempt = alpha ? new(WicImageCodec.EncodeToBytes(current, WicImageCodec.Png, 0), WicImageCodec.Png, current.PixelWidth, current.PixelHeight, null)
                                   : BestJpeg(current, targetBytes);
            if (best is null || attempt.Data.Length < best.Data.Length) best = attempt;
            if (attempt.Data.Length <= targetBytes) return attempt;

            // Too big at the lowest quality: scale down by the square root of the overshoot (area ∝ bytes), with margin.
            double k = Math.Sqrt((double)targetBytes / attempt.Data.Length) * 0.92;
            int w = (int)(current.PixelWidth * k), h = (int)(current.PixelHeight * k);
            if (Math.Min(w, h) < MinSide) break;
            current = WicImageCodec.Resize(current, w, h);
        }
        return best!; // closest we could get; the caller decides whether it's good enough
    }

    /// <summary>Highest JPEG quality in [<see cref="MinQuality"/>, <see cref="MaxQuality"/>] that fits; the minimum if none does.</summary>
    private static Result BestJpeg(BitmapSource image, long targetBytes)
    {
        var flat = WicImageCodec.FlattenOnWhite(image);
        byte[] Encode(int q) => WicImageCodec.EncodeToBytes(flat, WicImageCodec.Jpg, q);

        int lo = MinQuality, hi = MaxQuality;
        byte[] fit = Encode(lo);
        int fitQ = lo;
        if (fit.Length > targetBytes) return new(fit, WicImageCodec.Jpg, image.PixelWidth, image.PixelHeight, lo);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            var data = Encode(mid);
            if (data.Length <= targetBytes) { lo = mid; fit = data; fitQ = mid; }
            else hi = mid - 1;
        }
        return new(fit, WicImageCodec.Jpg, image.PixelWidth, image.PixelHeight, fitQ);
    }
}
