using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetVips;
using VImage = NetVips.Image;

namespace Chango.Conversion.Imaging;

/// <summary>
/// Photos through <b>libvips</b> (bundled; LGPL, one replaceable DLL). Compared with the Windows Imaging Component it
/// is several times faster, uses a fraction of the memory, runs on any thread, writes JPEG with mozjpeg, PNG palettes
/// with libimagequant and WebP with libwebp, and <b>keeps the metadata</b>: EXIF (camera, date taken, GPS location),
/// XMP, IPTC and the colour profile carry over into every format that can hold them. Pictures are turned upright on
/// load and the result's orientation tag is reset to match.
/// <para>Formats Windows reads through its own codecs only (HEIC, RAW, JPEG XR, ICO) and BMP stay with
/// <see cref="WicImageCodec"/>; <see cref="WithMetadataOf"/> still carries their source's metadata into the result
/// when libvips can read it.</para>
/// </summary>
public static class VipsCodec
{
    private static readonly Lazy<string?> Init = new(Initialize);

    /// <summary>True when libvips loaded on this PC.</summary>
    public static bool Available => Init.Value is null;

    /// <summary>Why libvips isn't available (null when it is), for diagnostics.</summary>
    public static string? InitError => Init.Value;

    private static string? Initialize()
    {
        try
        {
            if (!ModuleInitializer.VipsInitialized)
                return ModuleInitializer.Exception?.Message ?? "libvips didn't initialise";
            // Files change between operations (that's the point), so nothing may be answered from a cache.
            Cache.Max = 0;
            Cache.MaxFiles = 0;
            Cache.MaxMem = 0;
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException)
        {
            return ex.Message;
        }
    }

    /// <summary>Formats libvips reads in this build (HEIC isn't: it needs a HEVC decoder, which Windows supplies).</summary>
    private static readonly HashSet<string> Readable = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".webp", ".tif", ".tiff", ".gif", ".avif" };

    public static bool CanRead(string path) => Readable.Contains(Path.GetExtension(path)) && Available;

    public static bool CanWrite(ImageFormat format) =>
        (format == WicImageCodec.Jpg || format == WicImageCodec.Jfif || format == WicImageCodec.Png || format == WicImageCodec.Webp
         || format == WicImageCodec.Tiff || format == WicImageCodec.Gif) && Available;

    /// <summary>
    /// The picture, upright, with its metadata. Random access: turning a picture can't be streamed. Decoded into
    /// memory when <paramref name="inMemory"/> (it will be encoded several times).
    /// </summary>
    public static VImage Load(string path, bool inMemory = false)
    {
        // Lenient with originals (a slightly damaged photo still converts, as it opens in Photos); results are checked
        // strictly by Validate.
        var image = VImage.NewFromFile(path, access: Enums.Access.Random, failOn: Enums.FailOn.None);
        var upright = image.Autorot();
        if (!ReferenceEquals(upright, image)) image.Dispose();
        if (!inMemory) return upright;
        var copy = upright.CopyMemory();
        upright.Dispose();
        return copy;
    }

    /// <summary>Width and height from the header only (no decoding): a cheap check that a result is a sound picture.</summary>
    public static (int W, int H) Dimensions(string path)
    {
        using var image = VImage.NewFromFile(path, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
        return (image.Width, image.Height);
    }

    /// <summary>Null when <paramref name="path"/> decodes as <paramref name="width"/>×<paramref name="height"/>, otherwise why not.</summary>
    public static string? Validate(string path, int width, int height)
    {
        try
        {
            using var image = VImage.NewFromFile(path, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
            // Reading the last line proves the data is all there, at a fraction of a full decode's cost.
            using var lastLine = image.ExtractArea(0, image.Height - 1, image.Width, 1);
            lastLine.Avg();
            return image.Width == width && image.Height == height ? null : $"decoded as {image.Width}×{image.Height}, expected {width}×{height}";
        }
        catch (VipsException ex)
        {
            return ex.Message.Split('\n')[0];
        }
    }

    /// <summary>True when any pixel is less than fully opaque.</summary>
    public static bool HasTransparency(VImage image)
    {
        if (!image.HasAlpha()) return false;
        using var alpha = image.ExtractBand(image.Bands - 1);
        return alpha.Min() < MaxValue(image);
    }

    private static double MaxValue(VImage image) => image.Format switch
    {
        Enums.BandFormat.Ushort => 65535,
        Enums.BandFormat.Float or Enums.BandFormat.Double => 1,
        _ => 255,
    };

    /// <summary>8 bits per channel in sRGB (or greyscale), alpha kept: what JPEG and GIF need, and safe for any format.</summary>
    private static VImage To8Bit(VImage image)
    {
        if (image.Format == Enums.BandFormat.Uchar && image.Interpretation is Enums.Interpretation.Srgb or Enums.Interpretation.Bw) return image;
        bool grey = image.Interpretation is Enums.Interpretation.Bw or Enums.Interpretation.Grey16;
        return image.Colourspace(grey ? Enums.Interpretation.Bw : Enums.Interpretation.Srgb);
    }

    /// <summary>Formats without alpha: composite onto white instead of letting transparent pixels turn black.</summary>
    public static VImage FlattenOnWhite(VImage image)
    {
        var eight = To8Bit(image);
        if (!eight.HasAlpha()) return eight;
        return eight.Flatten(background: eight.Bands >= 3 ? [255, 255, 255] : [255]);
    }

    /// <summary>JPEG settings: the quick encoder (for searching), or mozjpeg's smallest (for the file that's kept).</summary>
    public static byte[] Jpeg(VImage image, int quality, bool smallest) => smallest
        ? image.JpegsaveBuffer(q: quality, optimizeCoding: true, trellisQuant: true, overshootDeringing: true, quantTable: 3,
            interlace: true, keep: Enums.ForeignKeep.All)
        : image.JpegsaveBuffer(q: quality, optimizeCoding: true, keep: Enums.ForeignKeep.All);

    /// <summary>
    /// WebP at effort 3: measured on 4K photos, as fast as the encoder Chango used before and 8% smaller. (Effort 4
    /// with "smart subsampling" was five times slower for a barely visible gain.)
    /// </summary>
    public static byte[] Webp(VImage image, int quality) =>
        image.WebpsaveBuffer(q: quality, effort: 3, keep: Enums.ForeignKeep.All);

    /// <summary>
    /// A 256-colour PNG (libimagequant, with dithering): screenshots, logos and graphics shrink by well over half and
    /// look the same. <paramref name="quality"/> is the most it may give up (0–100).
    /// </summary>
    public static byte[] PalettePng(VImage image, int quality) =>
        To8Bit(image).PngsaveBuffer(palette: true, q: quality, effort: 4, dither: 1.0, compression: 9, keep: Enums.ForeignKeep.All);

    /// <summary>Encodes in <paramref name="format"/> with every piece of metadata the format can hold.</summary>
    public static byte[] Encode(VImage image, ImageFormat format, int quality)
    {
        if (format == WicImageCodec.Jpg || format == WicImageCodec.Jfif)
        {
            using var flat = FlattenOnWhite(image);
            return Jpeg(flat, Math.Clamp(quality, 1, 100), smallest: false);
        }
        if (format == WicImageCodec.Png) return image.PngsaveBuffer(compression: 6, keep: Enums.ForeignKeep.All);
        if (format == WicImageCodec.Webp) return Webp(image, Math.Clamp(quality, 1, 100));
        if (format == WicImageCodec.Tiff) return image.TiffsaveBuffer(compression: Enums.ForeignTiffCompression.Lzw, keep: Enums.ForeignKeep.All);
        if (format == WicImageCodec.Gif)
        {
            using var eight = To8Bit(image);
            return eight.GifsaveBuffer(keep: Enums.ForeignKeep.All);
        }
        throw new NotSupportedException($"No libvips encoder for {format.Name}");
    }

    /// <summary>
    /// A picture made by Windows' imaging (the editor, rotation of formats libvips can't read …) as a libvips image,
    /// carrying the metadata of <paramref name="metadataSource"/> when libvips can read it.
    /// </summary>
    public static VImage FromBitmap(BitmapSource source, string? metadataSource)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        byte[] px = WicImageCodec.Pixels(bgra);
        bool alpha = false;
        for (int i = 3; i < px.Length; i += 4) if (px[i] != 255) { alpha = true; break; }

        using var raw = VImage.NewFromMemory(px, w, h, 4, Enums.BandFormat.Uchar);
        using var b = raw[0];
        using var g = raw[1];
        using var r = raw[2];
        using var rgb = alpha ? r.Bandjoin(g, b, raw[3]) : r.Bandjoin(g, b);
        var image = rgb.Copy(interpretation: Enums.Interpretation.Srgb);
        return metadataSource is null ? image : WithMetadataOf(image, metadataSource);
    }

    private static readonly string[] MetadataBlobs = ["exif-data", "xmp-data", "iptc-data"];
    private const string IccProfile = "icc-profile-data";

    /// <summary>
    /// <paramref name="image"/> with the EXIF, XMP, IPTC and colour profile of <paramref name="source"/> (read from
    /// its header only). The pixels are already upright, so the orientation is set to normal; the colour profile is
    /// carried only when it describes pixels like these (an RGB profile for colour, a grey one for greyscale).
    /// </summary>
    public static VImage WithMetadataOf(VImage image, string source)
    {
        if (!CanRead(source)) return image;
        VImage header;
        try { header = VImage.NewFromFile(source, access: Enums.Access.Sequential); }
        catch (VipsException) { return image; }
        using (header)
        {
            var result = image.Mutate(m =>
            {
                foreach (var name in MetadataBlobs)
                    if (header.Contains(name)) m.Set(GValue.BlobType, name, header.Get(name));
                if (header.Contains(IccProfile) && header.Get(IccProfile) is byte[] icc && ProfileFits(icc, image.Bands))
                    m.Set(GValue.BlobType, IccProfile, icc);
                m.Set(GValue.GIntType, "orientation", 1);
            });
            image.Dispose();
            return result;
        }
    }

    /// <summary>An ICC profile's colour space (bytes 16–19) matches the pixels: "RGB " for colour, "GRAY" for greyscale.</summary>
    private static bool ProfileFits(byte[] icc, int bands)
    {
        if (icc.Length < 20) return false;
        string space = System.Text.Encoding.ASCII.GetString(icc, 16, 4);
        return bands >= 3 ? space == "RGB " : space == "GRAY";
    }

    /// <summary>The picture as WPF pixels (BGRA32), for code that draws or measures with WPF.</summary>
    public static BitmapSource ToBitmap(VImage image)
    {
        using var eight = To8Bit(image);
        using var rgba = eight.Bands switch
        {
            1 => eight.Bandjoin(eight, eight, 255),
            2 => eight[0].Bandjoin(eight[0], eight[0], eight[1]),
            3 => eight.Bandjoin(255),
            _ => eight.Copy(),
        };
        using var bgra = rgba[2].Bandjoin(rgba[1], rgba[0], rgba[3]);
        byte[] px = bgra.WriteToMemory<byte>();
        var result = BitmapSource.Create(bgra.Width, bgra.Height, 96, 96, PixelFormats.Bgra32, null, px, bgra.Width * 4);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// A small copy (longest side at most <paramref name="side"/>), decoded at reduced size where the format allows
    /// (a JPEG is read at ½, ¼ or ⅛ scale): many times faster than decoding the whole picture first.
    /// </summary>
    public static BitmapSource Thumbnail(string path, int side)
    {
        using var thumb = VImage.Thumbnail(path, side, height: side, size: Enums.Size.Down);
        return ToBitmap(thumb);
    }
}
