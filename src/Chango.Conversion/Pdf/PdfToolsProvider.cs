using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chango.Conversion.Imaging;
using Chango.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Chango.Conversion.Pdf;

/// <summary>
/// PDF tools: merge several PDFs into one, split one into a PDF per page, rotate every page, and compress (the photos
/// inside are re-encoded smaller; text stays text). Built on PDFsharp 6.2.4 (MIT). Originals are never changed.
/// </summary>
public sealed class PdfToolsProvider : IOperationProvider
{
    public const string ProviderId = "pdf.tools";
    public string Id => ProviderId;

    private static readonly OperationGroup RotateGroup = new("pdf.rotate", "Rotate pages", "");

    /// <summary>Photos in a compressed PDF: at most this many pixels on the long side, at this JPEG quality.</summary>
    /// <summary>
    /// Photos inside a compressed PDF: at most 1600 px. mozjpeg (libvips) at 70 is both sharper and smaller than
    /// Windows' encoder at 65; Windows' encoder is the fallback when libvips can't load.
    /// </summary>
    private const int CompressMaxSide = 1600, CompressQuality = 70, WicCompressQuality = 65;

    private sealed record Rotation(string Key, string Label, int Degrees, bool Mirror);
    private static readonly Rotation[] Rotations =
    [
        new("right", "Rotate right", 90, false),
        new("left", "Rotate left", 270, true),
        new("180", "Rotate 180°", 180, false),
    ];

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Tools || files.IsEmpty || !files.All(PdfProvider.IsPdf)) yield break;
        if (files.Count > 1)
            yield return new("tool.pdf.merge", ProviderId, OperationMode.Tools, "Merge", "",
                "One PDF, in the order you picked", "", "Merging PDFs", 100);
        yield return new("tool.pdf.compress", ProviderId, OperationMode.Tools, "Compress", "",
            "Make the PDF smaller", "", "Compressing", 105);
        yield return new("tool.pdf.split", ProviderId, OperationMode.Tools, "Split", "",
            "A PDF for every page", "", "Splitting", 110);
        int order = 120;
        foreach (var r in Rotations)
            yield return new($"tool.pdf.rotate.{r.Key}", ProviderId, OperationMode.Tools, r.Label, "",
                "Every page", "", "Rotating pages", order++, r.Mirror, RotateGroup);
    }

    public Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct) => StaWorker.RunAsync(() =>
    {
        if (operation.Id == "tool.pdf.merge")
        {
            try
            {
                string output = Merge(files, context, ct);
                for (int i = 0; i < files.Count; i++)
                    progress.Report(new FileOutcome(files[i], OutcomeStatus.Succeeded, i == 0 ? output : null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { foreach (var f in files) progress.Report(new FileOutcome(f, OutcomeStatus.Failed, Message: Friendly(ex))); }
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                progress.Report(operation.Id switch
                {
                    "tool.pdf.split" => Split(file, context, ct),
                    "tool.pdf.compress" => Compress(file, context, ct),
                    var id => Rotate(file, Rotations.Single(r => id == $"tool.pdf.rotate.{r.Key}"), context, ct),
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex))); }
        }
    }, "Chango PDF tools");

    // ------------------------------------------------------------------ operations

    private static string Merge(FileSet files, OperationContext context, CancellationToken ct)
    {
        var output = new PdfDocument();
        DocInfo.FromPdf(files[0]).ApplyTo(output); // the first document's title, author, keywords and date
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var input = Open(files[i], PdfDocumentOpenMode.Import);
            foreach (var page in input.Pages) output.AddPage(page);
            context.ReportFileFraction?.Invoke((i + 1.0) / files.Count);
        }
        return SafeOutput.Write(files[0], context.Output, ".pdf", true, s => output.Save(s, closeStream: false), PdfProvider.ValidatePdf, ct);
    }

    /// <summary>"report.pdf" → a "report (pages)" folder with "Page 01.pdf" … next to it.</summary>
    private static FileOutcome Split(string file, OperationContext context, CancellationToken ct)
    {
        using var input = Open(file, PdfDocumentOpenMode.Import);
        int count = input.PageCount;
        if (count < 2) return new(file, OutcomeStatus.Skipped, Message: "This PDF has only one page");
        int digits = count.ToString().Length;
        var info = DocInfo.FromPdf(file);
        string folder = SafeOutput.WriteFolder(file, context.Output, $"{Path.GetFileNameWithoutExtension(file)} (pages)", dir =>
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var one = new PdfDocument();
                info.ApplyTo(one);
                one.AddPage(input.Pages[i]);
                one.Save(Path.Combine(dir, $"Page {(i + 1).ToString().PadLeft(digits, '0')}.pdf"));
                context.ReportFileFraction?.Invoke((i + 1.0) / count);
            }
        }, ct, dateContents: true);
        return new(file, OutcomeStatus.Succeeded, folder);
    }

    private static FileOutcome Rotate(string file, Rotation rotation, OperationContext context, CancellationToken ct)
    {
        using var doc = Open(file, PdfDocumentOpenMode.Modify);
        foreach (var page in doc.Pages) page.Rotate = ((page.Rotate + rotation.Degrees) % 360 + 360) % 360;
        string output = SafeOutput.Write(file, context.Output, null, true, s => doc.Save(s, closeStream: false), PdfProvider.ValidatePdf, ct);
        return new(file, OutcomeStatus.Succeeded, output);
    }

    /// <summary>
    /// Re-encodes the JPEG photos inside the PDF (at most <see cref="CompressMaxSide"/> px, quality
    /// <see cref="CompressQuality"/>) and compresses everything else as tightly as possible. Text, vector graphics and
    /// other images are untouched. Kept only if it actually got smaller.
    /// </summary>
    private static FileOutcome Compress(string file, OperationContext context, CancellationToken ct)
    {
        long before = new FileInfo(file).Length;
        using var doc = Open(file, PdfDocumentOpenMode.Modify);
        doc.Options.CompressContentStreams = true;
        doc.Options.NoCompression = false;
        doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        doc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;

        var images = doc.Internals.GetAllObjects().OfType<PdfDictionary>()
            .Where(d => d.Elements.GetName("/Subtype") == "/Image" && d.Stream is not null).ToList();
        // Which images qualify is read here; the re-encoding (pure work on bytes) runs on every core; the PDF objects
        // are only changed back on this thread, because PDFsharp isn't thread-safe.
        var jobs = images.Select(Candidate).ToArray();
        var shrunk = new Shrunk?[images.Count];
        int done = 0;
        void Report() => context.ReportFileFraction?.Invoke(Interlocked.Increment(ref done) / (double)Math.Max(1, images.Count) * 0.9);
        if (VipsCodec.Available)
        {
            Parallel.For(0, images.Count, new ParallelOptions { CancellationToken = ct }, i =>
            {
                if (jobs[i] is { } job)
                    try { shrunk[i] = ShrinkWithVips(job); }
                    catch (Exception) { } // an image that can't be re-encoded is left exactly as it was
                Report();
            });
        }
        else
        {
            for (int i = 0; i < images.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (jobs[i] is { } job)
                    try { shrunk[i] = ShrinkWithWic(job); }
                    catch (Exception) { }
                Report();
            }
        }
        for (int i = 0; i < images.Count; i++)
        {
            if (shrunk[i] is not { } r) continue;
            var image = images[i];
            image.Stream.Value = r.Data;
            image.Elements.SetInteger("/Width", r.Width);
            image.Elements.SetInteger("/Height", r.Height);
            image.Elements.SetName("/Filter", "/DCTDecode");
            image.Elements.Remove("/DecodeParms");
            image.Elements.SetInteger("/Length", r.Data.Length);
        }

        using var buffer = new MemoryStream();
        doc.Save(buffer, closeStream: false);
        if (buffer.Length >= before * 0.97)
            return new(file, OutcomeStatus.Skipped, Message: "This PDF is already as small as it can get");
        string output = SafeOutput.Write(file, context.Output, null, true, s => buffer.WriteTo(s), PdfProvider.ValidatePdf, ct);
        return new(file, OutcomeStatus.Succeeded, output);
    }

    private sealed record ShrinkJob(byte[] Jpeg, int Components);
    private sealed record Shrunk(byte[] Data, int Width, int Height);

    /// <summary>A plain RGB or greyscale JPEG image object (the kind that can be re-encoded safely), or null.</summary>
    private static ShrinkJob? Candidate(PdfDictionary image)
    {
        var filter = image.Elements["/Filter"];
        bool jpeg = filter is PdfName { Value: "/DCTDecode" } || filter is PdfArray { Elements.Count: 1 } a && a.Elements[0] is PdfName { Value: "/DCTDecode" };
        if (!jpeg || image.Elements.GetInteger("/BitsPerComponent") != 8) return null;
        if (image.Elements.ContainsKey("/Decode") || image.Elements.ContainsKey("/Mask")) return null; // keep exact colour and masking
        int components = Components(image.Elements["/ColorSpace"]);
        if (components is not (1 or 3)) return null; // CMYK, indexed and the like stay as they are
        return new ShrinkJob(image.Stream.Value, components);
    }

    /// <summary>
    /// The image at most <see cref="CompressMaxSide"/> px, re-encoded with mozjpeg (baseline, so every PDF reader and
    /// printer takes it); null unless it saves at least a tenth. EXIF orientation is ignored: in a PDF, the page
    /// places the image.
    /// </summary>
    private static Shrunk? ShrinkWithVips(ShrinkJob job)
    {
        using var decoded = NetVips.Image.NewFromBuffer(job.Jpeg, access: NetVips.Enums.Access.Sequential);
        if (decoded.Bands != job.Components) return null;
        double k = Math.Min(1.0, (double)CompressMaxSide / Math.Max(decoded.Width, decoded.Height));
        using var scaled = k < 1.0 ? decoded.Resize(k, kernel: NetVips.Enums.Kernel.Lanczos3) : decoded.Copy();
        byte[] smaller = scaled.JpegsaveBuffer(q: CompressQuality, optimizeCoding: true, trellisQuant: true, overshootDeringing: true,
            quantTable: 3, keep: NetVips.Enums.ForeignKeep.None);
        return smaller.Length < job.Jpeg.Length * 0.9 ? new Shrunk(smaller, scaled.Width, scaled.Height) : null;
    }

    /// <summary>The same with Windows' encoder (on this STA thread).</summary>
    private static Shrunk? ShrinkWithWic(ShrinkJob job)
    {
        BitmapSource bitmap;
        using (var ms = new MemoryStream(job.Jpeg, writable: false))
            bitmap = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        bitmap = new FormatConvertedBitmap(bitmap, job.Components == 1 ? PixelFormats.Gray8 : PixelFormats.Bgr24, null, 0);
        double k = Math.Min(1.0, (double)CompressMaxSide / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        if (k < 1.0) bitmap = new TransformedBitmap(bitmap, new ScaleTransform(k, k));
        byte[] smaller = WicImageCodec.EncodeToBytes(bitmap, WicImageCodec.Jpg, WicCompressQuality);
        return smaller.Length < job.Jpeg.Length * 0.9 ? new Shrunk(smaller, bitmap.PixelWidth, bitmap.PixelHeight) : null;
    }

    private static int Components(PdfItem? colorSpace)
    {
        if (colorSpace is PdfReference r) colorSpace = r.Value;
        return colorSpace switch
        {
            PdfName { Value: "/DeviceRGB" } => 3,
            PdfName { Value: "/DeviceGray" } => 1,
            PdfArray { Elements.Count: 2 } a when a.Elements[0] is PdfName { Value: "/ICCBased" } =>
                (a.Elements[1] is PdfReference icc ? icc.Value : a.Elements[1]) is PdfDictionary d ? d.Elements.GetInteger("/N") : 0,
            _ => 0,
        };
    }

    // ------------------------------------------------------------------ helpers

    private static PdfDocument Open(string file, PdfDocumentOpenMode mode)
    {
        try { return PdfReader.Open(file, mode); }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("This PDF is password-protected.");
        }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        InvalidDataException => ex.Message,
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        PdfReaderException => "This PDF couldn't be read (it may be damaged).",
        _ => ImageConversionProvider.Friendly(ex),
    };
}
