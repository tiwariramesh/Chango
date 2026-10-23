using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Svg;
using Svg.Skia;

namespace OrbitDrop.Conversion.Imaging;

/// <summary>
/// Reads SVG drawings (through Svg.Skia, MIT) as images, so they convert to PNG, JPG, WebP and the rest. SVGs are
/// vectors, so they're drawn big enough to stay sharp: small icons come out 1024 px on their long side, larger
/// drawings at twice their size, never more than 4096 px.
/// </summary>
/// <remarks>
/// An SVG is a document that can point elsewhere. Nothing here ever leaves the file: no images or elements from the
/// internet or from other files (no tracking pixels, nothing read from the disk), no DTDs (no "billion laughs"
/// entity bombs) and no scripts.
/// </remarks>
public static class SvgCodec
{
    private const int SmallTarget = 1024, MaxSide = 4096;

    static SvgCodec()
    {
        SvgDocument.DisableDtdProcessing = true;
        SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
        SvgDocument.ResolveExternalImages = ExternalType.None;
        SvgDocument.ResolveExternalElements = ExternalType.None;
    }

    /// <summary>The drawing as a BGRA32 bitmap with transparency, ready for any encoder.</summary>
    public static BitmapSource Load(string path)
    {
        using var svg = new SKSvg();
        svg.Settings.EnableJavaScript = false;
        svg.Settings.EnableExternalJavaScript = false;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            svg.Load(stream);
        var picture = svg.Picture ?? throw new InvalidDataException("The SVG has nothing to draw.");
        var bounds = picture.CullRect;
        if (!(bounds.Width > 0 && bounds.Height > 0)) throw new InvalidDataException("The SVG has no size.");

        float longSide = Math.Max(bounds.Width, bounds.Height);
        float scale = Math.Min(Math.Max(2f, SmallTarget / longSide), MaxSide / longSide);
        int w = Math.Max(1, (int)Math.Round(bounds.Width * scale)), h = Math.Max(1, (int)Math.Round(bounds.Height * scale));

        // Skia draws premultiplied; WPF converts that to the straight alpha every other part of OrbitDrop uses.
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }
        var premul = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bitmap.Bytes, bitmap.RowBytes);
        var bgra = new FormatConvertedBitmap(premul, PixelFormats.Bgra32, null, 0);
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, WicImageCodec.Pixels(bgra), w * 4);
        result.Freeze();
        return result;
    }
}
