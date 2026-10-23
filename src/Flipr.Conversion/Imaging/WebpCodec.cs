using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Flipr.Conversion.Imaging;

/// <summary>
/// WebP through SkiaSharp (MIT, bundles Google's libwebp). Windows can only read WebP, and only with the Store
/// extension installed, so writing always goes through here, and reading falls back to it when WIC can't.
/// </summary>
public static class WebpCodec
{
    public static void Encode(BitmapSource image, Stream stream, int quality)
    {
        BitmapSource bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var skImage = SKImage.FromPixelCopy(info, WicImageCodec.Pixels(bgra), w * 4)
            ?? throw new InvalidDataException("The image couldn't be prepared for WebP.");
        using var data = skImage.Encode(SKEncodedImageFormat.Webp, Math.Clamp(quality, 1, 100))
            ?? throw new InvalidDataException("The WebP encoder failed.");
        data.SaveTo(stream);
    }

    /// <summary>The file as BGRA32, or null when it isn't an image Skia can read.</summary>
    public static BitmapSource? TryDecode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var codec = SKCodec.Create(stream);
        if (codec is null) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var r = codec.GetPixels(info, bitmap.GetPixels());
        if (r is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return null;
        var result = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Bgra32, null, bitmap.Bytes, info.RowBytes);
        result.Freeze();
        return result;
    }
}
