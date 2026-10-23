using System.Windows.Media.Imaging;

namespace Flipr.Conversion.Imaging;

/// <summary>
/// Quick, honest guesses at result sizes for the wheel's hint. A small copy of the picture (640 px) is encoded in each
/// format; when the original's own format can be encoded too, the original's real size is scaled by the ratio between
/// the two (that keeps the original's compression in the picture); otherwise the small result is scaled up by pixels.
/// </summary>
public static class SizeEstimate
{
    private const int SampleSide = 640;
    /// <summary>Bigger pictures compress a little better per pixel than small copies of them.</summary>
    private const double PixelExponent = 0.9;

    /// <summary>Estimated bytes per output format (by <see cref="ImageFormat.Name"/>). Must run on an STA thread.</summary>
    public static Dictionary<string, long> Formats(string path, int jpegQuality)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        var source = WicImageCodec.FormatOf(path);
        if (source is null) return result;
        long size = new FileInfo(path).Length;
        var image = WicImageCodec.Load(path);
        var sample = WicImageCodec.FitLongSide(image.PixelWidth, image.PixelHeight, SampleSide) is { } fit
            ? WicImageCodec.Resize(image, fit.W, fit.H) : image;
        double pixelScale = Math.Pow((double)image.PixelWidth * image.PixelHeight / ((double)sample.PixelWidth * sample.PixelHeight), PixelExponent);

        long Encoded(ImageFormat f) => WicImageCodec.EncodeToBytes(sample, f, jpegQuality).LongLength;
        bool sourceEncodable = WicImageCodec.OutputFormats.Contains(source);
        long reference = sourceEncodable ? Encoded(source) : 0;

        foreach (var target in WicImageCodec.OutputFormats)
        {
            if (target == source) continue;
            bool jpegFamily = (source == WicImageCodec.Jpg || source == WicImageCodec.Jfif) && (target == WicImageCodec.Jpg || target == WicImageCodec.Jfif);
            long estimate = jpegFamily ? size // a rename: the very same bytes
                : sourceEncodable && reference > 0 ? (long)(size * ((double)Encoded(target) / reference))
                : (long)(Encoded(target) * pixelScale);
            result[target.Name] = Math.Max(1, estimate);
        }
        return result;
    }

    /// <summary>Pixel size without decoding the whole picture (header only where Windows can read it).</summary>
    public static (int W, int H) Dimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            int orientation = WicImageCodec.Orientation(frame);
            return orientation is >= 5 and <= 8 ? (frame.PixelHeight, frame.PixelWidth) : (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            var image = WicImageCodec.Load(path); // SVG, or WebP without the Windows codec
            return (image.PixelWidth, image.PixelHeight);
        }
    }

    /// <summary>The same picture at fewer pixels: its size shrinks a little less than the pixel count does.</summary>
    public static long Resized(long size, double pixelFraction) => Math.Max(1, (long)(size * Math.Pow(Math.Clamp(pixelFraction, 0, 1), PixelExponent)));
}
