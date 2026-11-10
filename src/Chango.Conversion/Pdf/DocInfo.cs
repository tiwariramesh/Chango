using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using VImage = NetVips.Image;

namespace Chango.Conversion.Pdf;

/// <summary>
/// The document details that travel from an original into what Chango makes of it: a PDF's title, author, subject,
/// keywords and creation date into a merged, split or converted file; a photo's photographer and date taken into the
/// PDF made from it.
/// </summary>
public sealed partial record DocInfo(string? Title = null, string? Author = null, string? Subject = null, string? Keywords = null,
    DateTime? Created = null)
{
    public static readonly DocInfo Empty = new();

    public bool IsEmpty => this == Empty;

    /// <summary>A PDF's document information (read without loading its pages); empty if it can't be read.</summary>
    public static DocInfo FromPdf(string path)
    {
        try
        {
            // PdfPig parses lazily: opening reads the trailer and Info dictionary, not the pages.
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            var info = doc.Information;
            return new DocInfo(Blank(info.Title), Blank(info.Author), Blank(info.Subject), Blank(info.Keywords),
                info.GetCreatedDateTimeOffset()?.LocalDateTime);
        }
        catch (Exception) { return Empty; }
    }

    /// <summary>A photo's photographer (EXIF Artist), description and date taken, as document details.</summary>
    public static DocInfo FromPhoto(string path)
    {
        if (!Imaging.VipsCodec.CanRead(path)) return Empty;
        try
        {
            using var image = VImage.NewFromFile(path, access: NetVips.Enums.Access.Sequential);
            var fields = image.GetFields();
            string? Field(string name) => fields.Contains(name) && image.Get(name) is string s ? Blank(ExifValue(s)) : null;
            DateTime? taken = Field("exif-ifd2-DateTimeOriginal") is { } t
                && DateTime.TryParseExact(t, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
            return new DocInfo(null, Field("exif-ifd0-Artist"), Field("exif-ifd0-ImageDescription"), null, taken);
        }
        catch (Exception) { return Empty; }
    }

    /// <summary>libvips shows EXIF strings as "value (value, ASCII, 9 components, 9 bytes)": the value alone.</summary>
    private static string ExifValue(string shown) => ExifSuffix().Replace(shown, "").Trim();

    [GeneratedRegex(@"\s*\([^()]*, [A-Za-z]+, \d+ components?, \d+ bytes\)$")]
    private static partial Regex ExifSuffix();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Sets these details on a PDF being made with PDFsharp (blank ones are left as they are).</summary>
    public void ApplyTo(PdfSharp.Pdf.PdfDocument doc)
    {
        if (Title is { } title) doc.Info.Title = title;
        if (Author is { } author) doc.Info.Author = author;
        if (Subject is { } subject) doc.Info.Subject = subject;
        if (Keywords is { } keywords) doc.Info.Keywords = keywords;
        if (Created is { } created) doc.Info.CreationDate = created;
    }

    /// <summary>Sets these details as a Word, Excel or PowerPoint file's properties (File → Info in Office).</summary>
    public void ApplyTo(OpenXmlPackage package)
    {
        var p = package.PackageProperties;
        if (Title is { } title) p.Title = title;
        if (Author is { } author) p.Creator = author;
        if (Subject is { } subject) p.Subject = subject;
        if (Keywords is { } keywords) p.Keywords = keywords;
        if (Created is { } created) p.Created = created.ToUniversalTime();
    }

    /// <summary>The entries of a PDF Info dictionary for these details ("/Author (…) /CreationDate (D:…)").</summary>
    public string PdfInfoEntries()
    {
        var sb = new StringBuilder();
        void Add(string key, string? value) { if (value is not null) sb.Append($" /{key} {PdfString(value)}"); }
        Add("Title", Title);
        Add("Author", Author);
        Add("Subject", Subject);
        Add("Keywords", Keywords);
        if (Created is { } c)
        {
            var local = new DateTimeOffset(c.Kind == DateTimeKind.Utc ? c.ToLocalTime() : c);
            var offset = local.Offset;
            sb.Append($" /CreationDate (D:{local:yyyyMMddHHmmss}{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}')");
        }
        return sb.ToString();
    }

    /// <summary>A PDF text string: plain ASCII as a literal, anything else as UTF-16 with a byte-order mark (hex).</summary>
    public static string PdfString(string s)
    {
        if (s.All(c => c >= 32 && c < 127))
            return "(" + s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
        return "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(s)) + ">";
    }
}
