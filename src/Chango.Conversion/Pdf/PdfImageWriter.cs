using System.Globalization;
using System.Text;

namespace Chango.Conversion.Pdf;

/// <summary>A JPEG-compressed image ready to place on a page. <see cref="Gray"/> selects DeviceGray over DeviceRGB.</summary>
public sealed record PdfImage(byte[] Jpeg, int Width, int Height, bool Gray = false);

/// <summary>
/// Writes a PDF with one image per page (PDF 1.4, images as DCTDecode streams). The page keeps the image's shape,
/// sized so its longer side is A4's long side (842 pt), which prints and views well. No dependencies.
/// </summary>
public static class PdfImageWriter
{
    public const double LongSide = 842;

    public static void Write(Stream output, IReadOnlyList<PdfImage> images, string? title = null)
    {
        if (images.Count == 0) throw new ArgumentException("A PDF needs at least one image.", nameof(images));
        var offsets = new List<long>();
        var w = new Counter(output);
        void Raw(string s) => w.Write(Encoding.ASCII.GetBytes(s));
        void Begin(int id) { offsets.Add(w.Position); Raw($"{id} 0 obj\n"); }

        Raw("%PDF-1.4\n");
        w.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]); // binary marker: tells transfer tools this isn't text
        // Object numbers: 1 catalog, 2 pages, 3 info, then (page, contents, image) per image.
        int PageId(int k) => 4 + k * 3;
        Begin(1); Raw("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Begin(2);
        Raw($"<< /Type /Pages /Count {images.Count} /Kids [{string.Join(" ", images.Select((_, k) => $"{PageId(k)} 0 R"))}] >>\nendobj\n");
        Begin(3);
        Raw($"<< /Producer (Chango){(title is null ? "" : $" /Title ({Escape(title)})")} >>\nendobj\n");

        for (int k = 0; k < images.Count; k++)
        {
            var img = images[k];
            double scale = LongSide / Math.Max(img.Width, img.Height);
            string pw = N(img.Width * scale), ph = N(img.Height * scale);
            int page = PageId(k), contents = page + 1, xobject = page + 2;

            Begin(page);
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pw} {ph}] /Resources << /XObject << /Im0 {xobject} 0 R >> >> /Contents {contents} 0 R >>\nendobj\n");

            string draw = $"q {pw} 0 0 {ph} 0 0 cm /Im0 Do Q";
            Begin(contents);
            Raw($"<< /Length {draw.Length} >>\nstream\n{draw}\nendstream\nendobj\n");

            Begin(xobject);
            Raw($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace /{(img.Gray ? "DeviceGray" : "DeviceRGB")} " +
                $"/BitsPerComponent 8 /Filter /DCTDecode /Length {img.Jpeg.Length} >>\nstream\n");
            w.Write(img.Jpeg);
            Raw("\nendstream\nendobj\n");
        }

        long xref = w.Position;
        Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Raw($"{o:D10} 00000 n \n");
        Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string s)
    {
        // PDF literal strings: escape \ ( ) and keep it to printable ASCII (titles are file names).
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c is '\\' or '(' or ')') sb.Append('\\').Append(c);
            else if (c >= 32 && c < 127) sb.Append(c);
            else sb.Append('?');
        }
        return sb.ToString();
    }

    /// <summary>Tracks the byte position for the cross-reference table (output streams may not support Position).</summary>
    private sealed class Counter(Stream inner)
    {
        public long Position { get; private set; }
        public void Write(byte[] data) { inner.Write(data); Position += data.Length; }
    }
}
