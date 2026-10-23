using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flipr.Conversion.Imaging;
using Flipr.Core;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Flipr.Conversion.Pdf;

/// <summary>
/// Images to PDF (one each, or several combined into one) and PDF pages to PNG/JPG. PDFs are written by
/// <see cref="PdfImageWriter"/>; pages are rendered by Windows' own PDF engine (Windows.Data.Pdf). Nothing to install.
/// </summary>
public sealed class PdfProvider : IOperationProvider
{
    public const string ProviderId = "pdf";
    public string Id => ProviderId;

    /// <summary>Pages render at twice their on-screen size (≈192 dpi): sharp, without enormous files.</summary>
    private const double RenderScale = 2.0;
    private const double MaxRenderSide = 6000;

    public static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (files.IsEmpty) yield break;
        if (files.All(WicImageCodec.IsSupportedInput))
        {
            if (mode == OperationMode.Convert)
                yield return new("convert.image.pdf", ProviderId, OperationMode.Convert, "PDF", null, "Convert to %1", "PDF", "Making PDF", 8);
            if (mode == OperationMode.Tools && files.Count > 1)
                yield return new("tool.image.pdf-combine", ProviderId, OperationMode.Tools, "Combine to PDF", "",
                    "One PDF, a page per image", "", "Making PDF", 145);
        }
        else if (mode == OperationMode.Convert && files.All(IsPdf))
        {
            yield return new("convert.pdf.png", ProviderId, OperationMode.Convert, "PNG", null, "Pages to %1", "PNG", "Saving pages as PNG", 1);
            yield return new("convert.pdf.jpg", ProviderId, OperationMode.Convert, "JPG", null, "Pages to %1", "JPG", "Saving pages as JPG", 2);
        }
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        switch (operation.Id)
        {
            case "convert.image.pdf":
                await StaWorker.RunAsync(() =>
                {
                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        Report(progress, file, () =>
                        {
                            var page = ToPdfImage(file, context.JpegQuality);
                            return SafeOutput.Write(file, context.Output, ".pdf", null,
                                s => PdfImageWriter.Write(s, [page], Path.GetFileNameWithoutExtension(file)), ValidatePdf, ct);
                        });
                    }
                }, "Flipr PDF");
                break;

            case "tool.image.pdf-combine":
                await StaWorker.RunAsync(() =>
                {
                    var pages = new List<PdfImage>();
                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        pages.Add(ToPdfImage(file, context.JpegQuality));
                        context.ReportFileFraction?.Invoke((double)pages.Count / files.Count);
                    }
                    string output = SafeOutput.Write(files[0], context.Output, ".pdf", "combined",
                        s => PdfImageWriter.Write(s, pages, Path.GetFileNameWithoutExtension(files[0])), ValidatePdf, ct);
                    // One PDF for the whole selection: the first file carries the output, the rest are part of it.
                    for (int i = 0; i < files.Count; i++)
                        progress.Report(new FileOutcome(files[i], OutcomeStatus.Succeeded, i == 0 ? output : null));
                }, "Flipr PDF");
                break;

            case "convert.pdf.png" or "convert.pdf.jpg":
            {
                var format = operation.Id.EndsWith("png", StringComparison.Ordinal) ? WicImageCodec.Png : WicImageCodec.Jpg;
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try { progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, await RenderPagesAsync(file, file, format, context, ct))); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex))); }
                }
                break;
            }
            default: throw new NotSupportedException(operation.Id);
        }
    }

    private static void Report(IProgress<FileOutcome> progress, string file, Func<string> run)
    {
        try { progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, run())); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: ImageConversionProvider.Friendly(ex))); }
    }

    /// <summary>
    /// An upright JPEG for the page. Plain JPEGs (no rotation flag, RGB or greyscale) go in byte for byte, so the PDF
    /// loses nothing; everything else is decoded, flattened onto white and encoded at the user's JPEG quality.
    /// </summary>
    public static PdfImage ToPdfImage(string file, int jpegQuality)
    {
        if (WicImageCodec.FormatOf(file)?.Name is "JPG" or "JFIF")
        {
            byte[] bytes = File.ReadAllBytes(file);
            using var ms = new MemoryStream(bytes, writable: false);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
            bool rgb = frame.Format == PixelFormats.Bgr24 || frame.Format == PixelFormats.Rgb24, gray = frame.Format == PixelFormats.Gray8;
            if (WicImageCodec.Orientation(frame) == 1 && (rgb || gray))
                return new PdfImage(bytes, frame.PixelWidth, frame.PixelHeight, gray);
        }
        var image = WicImageCodec.Load(file);
        return new PdfImage(WicImageCodec.EncodeToBytes(image, WicImageCodec.Jpg, Math.Max(jpegQuality, 85)), image.PixelWidth, image.PixelHeight);
    }

    /// <summary>
    /// One page → "doc.png"; several → a "doc (PNG)" folder with "Page 01.png" … Renders <paramref name="pdf"/>; the
    /// output is named after, and placed next to, <paramref name="file"/> (the same file, or the drawing a PDF was made from).
    /// </summary>
    public static async Task<string> RenderPagesAsync(string pdf, string file, ImageFormat format, OperationContext context, CancellationToken ct)
    {
        var storage = await StorageFile.GetFileFromPathAsync(pdf);
        var document = await PdfDocument.LoadFromFileAsync(storage);
        if (document.IsPasswordProtected) throw new InvalidDataException("This PDF is password-protected.");
        uint count = document.PageCount;
        string ext = format.Extension;
        bool png = format == WicImageCodec.Png;

        var pages = new List<byte[]>();
        for (uint i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = document.GetPage(i);
            double k = Math.Min(RenderScale, MaxRenderSide / Math.Max(page.Size.Width, page.Size.Height));
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width * k),
                DestinationHeight = (uint)Math.Round(page.Size.Height * k),
                BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
            });
            using var managed = stream.AsStreamForRead();
            using var copy = new MemoryStream();
            await managed.CopyToAsync(copy, ct);
            pages.Add(copy.ToArray());
            context.ReportFileFraction?.Invoke((i + 1.0) / count);
        }

        // Other formats are re-encoded from the PNG render (JPEG at the user's quality, on a white page background).
        byte[] Encode(byte[] pngBytes) => png ? pngBytes : Reencode(pngBytes, format, context.JpegQuality);

        if (count == 1)
            return SafeOutput.Write(file, context.Output, ext, null, s => s.Write(Encode(pages[0])), _ => null, ct);

        string name = $"{Path.GetFileNameWithoutExtension(file)} ({ext.TrimStart('.').ToUpperInvariant()})";
        int digits = count.ToString().Length;
        return SafeOutput.WriteFolder(file, context.Output, name, folder =>
        {
            for (int i = 0; i < pages.Count; i++)
                File.WriteAllBytes(Path.Combine(folder, $"Page {(i + 1).ToString().PadLeft(digits, '0')}{ext}"), Encode(pages[i]));
        }, ct);
    }

    private static byte[] Reencode(byte[] pngBytes, ImageFormat format, int quality)
    {
        byte[] result = [];
        StaWorker.RunAsync(() =>
        {
            using var ms = new MemoryStream(pngBytes);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            result = WicImageCodec.EncodeToBytes(bgra, format, quality);
        }).GetAwaiter().GetResult();
        return result;
    }

    public static string? ValidatePdf(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < 64) return "the PDF is too small";
        var head = new byte[5];
        fs.ReadExactly(head);
        if (System.Text.Encoding.ASCII.GetString(head) != "%PDF-") return "the output isn't a PDF";
        fs.Seek(-6, SeekOrigin.End);
        var tail = new byte[6];
        fs.ReadExactly(tail);
        return System.Text.Encoding.ASCII.GetString(tail).Contains("%%EOF") ? null : "the PDF is incomplete";
    }

    private static string Friendly(Exception ex) => ex switch
    {
        InvalidDataException => ex.Message,
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        _ when ex.HResult == unchecked((int)0x80004005) => "Windows couldn't open this PDF (it may be damaged or encrypted).",
        _ => ex.Message,
    };

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("PDF", "Images to PDF (one each, or combined) · PDF pages to PNG or JPG");
    }
}
