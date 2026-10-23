using System.Windows.Media.Imaging;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Conversion.Pdf;
using OrbitDrop.Core;

namespace OrbitDrop.Conversion.Documents;

/// <summary>
/// Documents (Word, Excel, PowerPoint, OpenDocument) to PDF, old formats to the current ones, and PDF to Word, through
/// the Microsoft Office or LibreOffice already on this PC (see <see cref="DocumentEngines"/>). CorelDRAW drawings to
/// SVG, PDF and images with OrbitDrop's own reader (see <see cref="CorelDraw"/>): nothing to install for those.
/// </summary>
public sealed class DocumentProvider : IOperationProvider
{
    public const string ProviderId = "document";
    public string Id => ProviderId;

    private sealed record Target(string Name, string Extension);

    private static readonly Target Pdf = new("PDF", ".pdf"), Docx = new("DOCX", ".docx"), Xlsx = new("XLSX", ".xlsx"),
        Pptx = new("PPTX", ".pptx"), Svg = new("SVG", ".svg"), Png = new("PNG", ".png"), Jpg = new("JPG", ".jpg"),
        Webp = new("WEBP", ".webp"), Tiff = new("TIFF", ".tiff"), Bmp = new("BMP", ".bmp");

    /// <summary>Wheel order of every target, across kinds.</summary>
    private static readonly Target[] All = [Pdf, Docx, Xlsx, Pptx, Svg, Png, Jpg, Webp, Tiff, Bmp];

    /// <summary>Drawing targets drawn from the drawing's SVG pages (sharp at any size).</summary>
    private static readonly Dictionary<Target, ImageFormat> Rendered = new()
    {
        [Png] = WicImageCodec.Png, [Jpg] = WicImageCodec.Jpg, [Webp] = WicImageCodec.Webp, [Tiff] = WicImageCodec.Tiff, [Bmp] = WicImageCodec.Bmp,
    };

    private static IEnumerable<Target> TargetsFor(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (DocumentEngines.KindOf(path) is not { } kind || !DocumentEngines.CanOpen(kind)) return [];
        return kind switch
        {
            DocumentKind.Text => ext == ".docx" ? [Pdf] : [Pdf, Docx],
            DocumentKind.Sheet => ext == ".xlsx" ? [Pdf] : [Pdf, Xlsx],
            DocumentKind.Slides => ext == ".pptx" ? [Pdf] : [Pdf, Pptx],
            DocumentKind.Drawing => [Pdf, Svg, Png, Jpg, Webp, Tiff, Bmp],
            DocumentKind.Pdf => [Docx],
            _ => [],
        };
    }

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Convert || files.IsEmpty || !files.All(f => DocumentEngines.KindOf(f) is not null)) yield break;
        // Only what every file can become (a mixed selection usually leaves just PDF).
        var common = files.Select(f => TargetsFor(f).ToHashSet()).Aggregate((a, b) => { a.IntersectWith(b); return a; });
        bool drawings = files.All(f => DocumentEngines.KindOf(f) == DocumentKind.Drawing);
        int order = 0;
        foreach (var t in All)
        {
            order++;
            if (!common.Contains(t)) continue;
            string id = drawings ? $"convert.drawing.{t.Name.ToLowerInvariant()}" : $"convert.document.{t.Name.ToLowerInvariant()}";
            yield return new OperationDescriptor(id, ProviderId, OperationMode.Convert, t.Name, null, "Convert to %1", t.Name,
                $"Converting to {t.Name}", order);
        }
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        var target = All.Single(t => operation.Id.EndsWith("." + t.Name.ToLowerInvariant(), StringComparison.Ordinal));
        using var engines = new DocumentEngines();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string output = DocumentEngines.KindOf(file) == DocumentKind.Drawing
                    ? await ConvertDrawingAsync(file, target, engines.NewWorkFolder(), context, ct)
                    : await ConvertDocumentAsync(engines, file, target, context, ct);
                progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex)));
            }
        }
    }

    private static async Task<string> ConvertDocumentAsync(DocumentEngines engines, string file, Target target, OperationContext context, CancellationToken ct)
    {
        string temp = await engines.ConvertToTempAsync(file, target.Extension, ct);
        return SafeOutput.Write(file, context.Output, target.Extension, null,
            s => { using var src = File.OpenRead(temp); src.CopyTo(s); },
            check => target == Pdf ? PdfProvider.ValidatePdf(check) : null, ct);
    }

    /// <summary>
    /// The drawing's pages as SVG, then: SVG as is; images drawn from it; PDF with one page per drawing page.
    /// One page → "poster.png"; several → a "poster (PNG)" folder with "Page 1.png" …
    /// </summary>
    private static async Task<string> ConvertDrawingAsync(string file, Target target, string work, OperationContext context, CancellationToken ct)
    {
        var pages = await CorelDraw.ToSvgPagesAsync(file, work, ct);
        string label = target.Name;

        if (target == Svg)
            return Save(file, pages.Select(p => (Func<byte[]>)(() => File.ReadAllBytes(p))).ToList(), ".svg", label, context, ct);

        // Drawing SVG needs an STA thread (WPF imaging).
        var images = new List<byte[]>();
        var pdfPages = new List<PdfImage>();
        await StaWorker.RunAsync(() =>
        {
            for (int i = 0; i < pages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                BitmapSource image = SvgCodec.Load(pages[i]);
                if (target == Pdf)
                    pdfPages.Add(new PdfImage(WicImageCodec.EncodeToBytes(image, WicImageCodec.Jpg, Math.Max(context.JpegQuality, 92)), image.PixelWidth, image.PixelHeight));
                else
                    images.Add(WicImageCodec.EncodeToBytes(image, Rendered[target], context.JpegQuality));
                context.ReportFileFraction?.Invoke((i + 1.0) / pages.Count);
            }
        }, "OrbitDrop CorelDRAW");

        if (target == Pdf)
            return SafeOutput.Write(file, context.Output, ".pdf", null,
                s => PdfImageWriter.Write(s, pdfPages, Path.GetFileNameWithoutExtension(file)), PdfProvider.ValidatePdf, ct);
        return Save(file, images.Select(b => (Func<byte[]>)(() => b)).ToList(), target.Extension, label, context, ct);
    }

    private static string Save(string file, IReadOnlyList<Func<byte[]>> pages, string extension, string label, OperationContext context, CancellationToken ct)
    {
        if (pages.Count == 1)
            return SafeOutput.Write(file, context.Output, extension, null, s => s.Write(pages[0]()), _ => null, ct);
        int digits = pages.Count.ToString().Length;
        return SafeOutput.WriteFolder(file, context.Output, $"{Path.GetFileNameWithoutExtension(file)} ({label})", folder =>
        {
            for (int i = 0; i < pages.Count; i++)
                File.WriteAllBytes(Path.Combine(folder, $"Page {(i + 1).ToString().PadLeft(digits, '0')}{extension}"), pages[i]());
        }, ct);
    }

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        if (CorelDraw.Available) yield return ("Drawings", "CorelDRAW (.cdr, .cmx) to PDF, SVG, PNG, JPG, WebP, TIFF, BMP · built in");
        var apps = DocumentEngines.Describe();
        yield return ("Documents", apps.Length == 0
            ? "Word, Excel and PowerPoint files convert when Microsoft Office or LibreOffice is installed"
            : $"Documents to PDF (and DOCX, XLSX, PPTX) · PDF to Word · using {string.Join(", ", apps)}");
    }

    private static string Friendly(Exception ex) => ex switch
    {
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        TimeoutException => "The conversion took too long and was stopped.",
        System.Runtime.InteropServices.COMException com when com.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            => "The file is password-protected.",
        System.Runtime.InteropServices.COMException com => $"Office couldn't convert this file: {com.Message.Trim()}",
        _ => ex.Message,
    };
}
