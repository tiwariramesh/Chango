using System.Globalization;
using Chango.Core;
using Windows.Storage;

namespace Chango.Tools.Metadata;

/// <summary>
/// Reads and writes file metadata through the Windows property system, the same data Explorer shows on the
/// Details tab. Works for every format Windows has a property handler for (JPEG, TIFF, MP3, M4A, MP4, FLAC, Office …).
/// </summary>
public static class FileProperties
{
    public enum Kind { Image, Audio, Video, Document, Other }

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jpe", ".jfif", ".tif", ".tiff", ".png", ".heic", ".heif", ".webp", ".bmp", ".gif" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".wma", ".flac", ".wav", ".aac" };
    private static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".3gp" };
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".xlsx", ".pptx", ".docm", ".xlsm", ".pptm", ".pdf" };

    /// <summary>Formats whose Windows property handler can write the editable fields.</summary>
    private static readonly HashSet<string> Writable = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jpe", ".jfif", ".tif", ".tiff", ".heic", ".mp3", ".m4a", ".wma", ".flac", ".mp4", ".m4v", ".wmv", ".docx", ".xlsx", ".pptx" };

    /// <summary>Formats that can carry GPS coordinates Windows can remove.</summary>
    public static readonly HashSet<string> LocationCapable = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jpe", ".jfif" };

    public static Kind KindOf(string path)
    {
        string ext = System.IO.Path.GetExtension(path);
        return Images.Contains(ext) ? Kind.Image : Audio.Contains(ext) ? Kind.Audio : Video.Contains(ext) ? Kind.Video
            : Documents.Contains(ext) ? Kind.Document : Kind.Other;
    }

    public static bool CanWrite(string path) => Writable.Contains(System.IO.Path.GetExtension(path));

    private static readonly string[] Keys =
    [
        "System.Title", "System.Author", "System.Comment", "System.Keywords", "System.Rating", "System.Copyright", "System.Subject",
        "System.Image.Dimensions", "System.Photo.CameraManufacturer", "System.Photo.CameraModel", "System.Photo.DateTaken",
        "System.Photo.ExposureTime", "System.Photo.FNumber", "System.Photo.ISOSpeed", "System.Photo.FocalLength", "System.Photo.LensModel",
        "System.GPS.LatitudeDecimal", "System.GPS.LongitudeDecimal", "System.GPS.Latitude", "System.GPS.Longitude",
        "System.GPS.LatitudeRef", "System.GPS.LongitudeRef",
        "System.Media.Duration", "System.Media.Year", "System.Video.FrameWidth", "System.Video.FrameHeight", "System.Video.FrameRate",
        "System.Video.TotalBitrate", "System.Audio.EncodingBitrate", "System.Audio.SampleRate", "System.Audio.ChannelCount",
        "System.Music.Artist", "System.Music.AlbumArtist", "System.Music.AlbumTitle", "System.Music.Genre", "System.Music.TrackNumber",
        "System.Document.PageCount", "System.Document.WordCount",
    ];

    public static async Task<MetadataSnapshot> ReadAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        IDictionary<string, object> p;
        try { p = await file.Properties.RetrievePropertiesAsync(Keys); }
        catch { p = new Dictionary<string, object>(); } // no property handler: facts from the file system only
        var info = new FileInfo(path);
        var kind = KindOf(path);
        var facts = new List<KeyValuePair<string, string>>();
        void Fact(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) facts.Add(new(label, value)); }

        Fact("Type", $"{System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} {kind.ToString().ToLowerInvariant()}".Replace(" other", " file"));
        Fact("Size", HumanSize(info.Length));
        switch (kind)
        {
            case Kind.Image:
                Fact("Dimensions", Str(p, "System.Image.Dimensions")?.Replace(" x ", " × "));
                Fact("Camera", Join(" ", Str(p, "System.Photo.CameraManufacturer"), Str(p, "System.Photo.CameraModel")));
                Fact("Lens", Str(p, "System.Photo.LensModel"));
                Fact("Taken", Get<DateTimeOffset>(p, "System.Photo.DateTaken") is { } taken ? taken.LocalDateTime.ToString("f", CultureInfo.CurrentCulture) : null);
                Fact("Exposure", Join(" · ",
                    Get<double>(p, "System.Photo.ExposureTime") is { } t and > 0 ? (t >= 1 ? $"{t:0.#} s" : $"1/{Math.Round(1 / t)} s") : null,
                    Get<double>(p, "System.Photo.FNumber") is { } f and > 0 ? $"ƒ/{f:0.#}" : null,
                    Get<ushort>(p, "System.Photo.ISOSpeed") is { } iso and > 0 ? $"ISO {iso}" : null,
                    Get<double>(p, "System.Photo.FocalLength") is { } fl and > 0 ? $"{fl:0} mm" : null));
                break;
            case Kind.Audio or Kind.Video:
                Fact("Length", Get<ulong>(p, "System.Media.Duration") is { } d and > 0 ? Duration(TimeSpan.FromTicks((long)d)) : null);
                if (kind == Kind.Video)
                {
                    Fact("Resolution", Get<uint>(p, "System.Video.FrameWidth") is { } w and > 0 && Get<uint>(p, "System.Video.FrameHeight") is { } h
                        ? $"{w} × {h}" : null);
                    Fact("Frame rate", Get<uint>(p, "System.Video.FrameRate") is { } fr and > 0 ? $"{fr / 1000.0:0.##} fps" : null);
                    Fact("Bitrate", Get<uint>(p, "System.Video.TotalBitrate") is { } vb and > 0 ? $"{vb / 1000:N0} kbps" : null);
                }
                else
                {
                    Fact("Bitrate", Get<uint>(p, "System.Audio.EncodingBitrate") is { } ab and > 0 ? $"{ab / 1000:N0} kbps" : null);
                    Fact("Sample rate", Get<uint>(p, "System.Audio.SampleRate") is { } sr and > 0 ? $"{sr / 1000.0:0.#} kHz" : null);
                    Fact("Channels", Get<uint>(p, "System.Audio.ChannelCount") switch { 1 => "Mono", 2 => "Stereo", { } n and > 2 => $"{n} channels", _ => null });
                }
                break;
            case Kind.Document:
                Fact("Pages", Get<int>(p, "System.Document.PageCount") is { } pages and > 0 ? pages.ToString("N0") : null);
                Fact("Words", Get<int>(p, "System.Document.WordCount") is { } words and > 0 ? words.ToString("N0") : null);
                break;
        }
        var location = Location(p);
        Fact("Location", location);
        Fact("Modified", info.LastWriteTime.ToString("f", CultureInfo.CurrentCulture));
        Fact("Created", info.CreationTime.ToString("f", CultureInfo.CurrentCulture));
        Fact("Folder", info.DirectoryName);

        var fields = new List<MetadataField>();
        if (CanWrite(path))
        {
            void Field(string key, string label, MetadataFieldKind k, string? hint = null) => fields.Add(new(key, label, k, ValueText(p, key, k), hint));
            Field("System.Title", "Title", MetadataFieldKind.Text);
            switch (kind)
            {
                case Kind.Audio:
                    Field("System.Music.Artist", "Artists", MetadataFieldKind.List, "Separate with ;");
                    Field("System.Music.AlbumTitle", "Album", MetadataFieldKind.Text);
                    Field("System.Music.AlbumArtist", "Album artist", MetadataFieldKind.Text);
                    Field("System.Music.Genre", "Genre", MetadataFieldKind.List, "Separate with ;");
                    Field("System.Media.Year", "Year", MetadataFieldKind.Number);
                    Field("System.Music.TrackNumber", "Track", MetadataFieldKind.Number);
                    break;
                case Kind.Video:
                    Field("System.Keywords", "Tags", MetadataFieldKind.List, "Separate with ;");
                    Field("System.Media.Year", "Year", MetadataFieldKind.Number);
                    break;
                case Kind.Document:
                    Field("System.Subject", "Subject", MetadataFieldKind.Text);
                    Field("System.Author", "Authors", MetadataFieldKind.List, "Separate with ;");
                    Field("System.Keywords", "Tags", MetadataFieldKind.List, "Separate with ;");
                    break;
                default:
                    Field("System.Author", "Authors", MetadataFieldKind.List, "Separate with ;");
                    Field("System.Keywords", "Tags", MetadataFieldKind.List, "Separate with ;");
                    Field("System.Copyright", "Copyright", MetadataFieldKind.Text);
                    Field("System.Photo.DateTaken", "Date taken", MetadataFieldKind.Date);
                    break;
            }
            Field("System.Rating", "Rating", MetadataFieldKind.Rating);
            Field("System.Comment", "Comments", MetadataFieldKind.MultiLine);
        }
        // Only offer to remove a location where Chango can do it losslessly.
        return new MetadataSnapshot(path, kind.ToString(), facts, fields, location is not null && LocationCapable.Contains(System.IO.Path.GetExtension(path)));
    }

    /// <summary>
    /// Applies the field changes in <paramref name="edit"/> to <paramref name="path"/> (a copy: callers never pass the
    /// original). GPS is read-only in the property system; <see cref="MetadataStripper.JpegWithoutLocation"/> removes it.
    /// </summary>
    public static async Task WriteAsync(string path, MetadataEdit edit)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var values = new List<KeyValuePair<string, object?>>();
        foreach (var (key, text) in edit.Changes)
            values.Add(new(key, string.IsNullOrWhiteSpace(text) ? null : Parse(key, text.Trim())));
        await file.Properties.SavePropertiesAsync(values!);
    }

    /// <summary>Converts edited text to the property's type. Public for unit tests.</summary>
    public static object Parse(string key, string text) => key switch
    {
        "System.Author" or "System.Keywords" or "System.Music.Artist" or "System.Music.Genre" =>
            text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        "System.Rating" => RatingToProperty(int.Parse(text, CultureInfo.InvariantCulture)),
        "System.Media.Year" or "System.Music.TrackNumber" => uint.Parse(text, NumberStyles.None, CultureInfo.CurrentCulture),
        "System.Photo.DateTaken" => new DateTimeOffset(DateTime.Parse(text, CultureInfo.CurrentCulture)),
        _ => text,
    };

    /// <summary>Stars (0–5) to the Windows rating scale (1, 25, 50, 75, 99), and back.</summary>
    public static uint RatingToProperty(int stars) => stars switch { <= 0 => 0, 1 => 1, 2 => 25, 3 => 50, 4 => 75, _ => 99 };
    public static int RatingFromProperty(uint value) => value switch { 0 => 0, < 13 => 1, < 38 => 2, < 63 => 3, < 88 => 4, _ => 5 };

    private static string? ValueText(IDictionary<string, object> p, string key, MetadataFieldKind kind)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            string[] list => string.Join("; ", list),
            DateTimeOffset dt => dt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
            uint u when kind == MetadataFieldKind.Rating => RatingFromProperty(u).ToString(CultureInfo.InvariantCulture),
            uint u when u == 0 => null,
            _ => Convert.ToString(v, CultureInfo.CurrentCulture),
        };
    }

    private static string? Location(IDictionary<string, object> p)
    {
        double? lat = Get<double>(p, "System.GPS.LatitudeDecimal"), lon = Get<double>(p, "System.GPS.LongitudeDecimal");
        if (lat is null || lon is null)
        {
            lat = Dms(p, "System.GPS.Latitude", "System.GPS.LatitudeRef", "S");
            lon = Dms(p, "System.GPS.Longitude", "System.GPS.LongitudeRef", "W");
        }
        return lat is { } a && lon is { } b ? $"{a:0.#####}, {b:0.#####}" : null;
    }

    private static double? Dms(IDictionary<string, object> p, string key, string refKey, string negative)
    {
        if (!p.TryGetValue(key, out var v) || v is not double[] { Length: 3 } d) return null;
        double value = d[0] + d[1] / 60 + d[2] / 3600;
        return Str(p, refKey) is { } r && r.StartsWith(negative, StringComparison.OrdinalIgnoreCase) ? -value : value;
    }

    private static T? Get<T>(IDictionary<string, object> p, string key) where T : struct =>
        p.TryGetValue(key, out var v) && v is T t ? t : null;

    private static string? Str(IDictionary<string, object> p, string key) => p.TryGetValue(key, out var v) ? v switch
    {
        string s when !string.IsNullOrWhiteSpace(s) => s.Trim(),
        string[] list when list.Length > 0 => string.Join("; ", list),
        _ => null,
    } : null;

    private static string? Join(string separator, params string?[] parts)
    {
        var present = parts.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return present.Count == 0 ? null : string.Join(separator, present);
    }

    public static string HumanSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
    };

    private static string Duration(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
