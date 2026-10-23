using System.Text;
using Flipr.Core;

namespace Flipr.Conversion.Pdf;

/// <summary>
/// PDF to PowerPoint (a slide per page), Excel (tables rebuilt from the text layout), plain text and Markdown. All of
/// it runs inside Flipr (<see cref="PdfText"/>, <see cref="OfficeWriters"/>); Office isn't needed. PDF to Word stays
/// with <see cref="Documents.DocumentProvider"/>, where Word rebuilds the layout.
/// </summary>
public sealed class PdfContentProvider : IOperationProvider
{
    public const string ProviderId = "pdf.content";
    public string Id => ProviderId;

    private sealed record Target(string Label, string Extension, string Hint, string Progress, int Order);

    private static readonly Target[] Targets =
    [
        new("PPTX", ".pptx", "Convert to %1 (a slide per page)", "Making slides", 3),
        new("XLSX", ".xlsx", "Tables to %1 (a sheet per page)", "Extracting tables", 4),
        new("TXT", ".txt", "Text to %1", "Extracting text", 5),
        new("MD", ".md", "Convert to %1 (Markdown)", "Converting to Markdown", 6),
    ];

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Convert || files.IsEmpty || !files.All(PdfProvider.IsPdf)) yield break;
        foreach (var t in Targets)
            yield return new($"convert.pdf{t.Extension}", ProviderId, OperationMode.Convert, t.Label, null, t.Hint, t.Label, t.Progress, t.Order);
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        var target = Targets.Single(t => operation.Id == $"convert.pdf{t.Extension}");
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string output = await ConvertAsync(file, target.Extension, context, ct);
                progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex))); }
        }
    }

    private static async Task<string> ConvertAsync(string file, string extension, OperationContext context, CancellationToken ct)
    {
        var fraction = context.ReportFileFraction;
        switch (extension)
        {
            case ".pptx":
            {
                var pages = await PdfProvider.RenderPngPagesAsync(file, f => fraction?.Invoke(f * 0.9), ct);
                return WriteOffice(file, ".pptx", s => OfficeWriters.WritePictureDeck(s, pages), context, ct);
            }
            case ".xlsx":
            {
                var tables = await Task.Run(() => PdfText.ToTables(file, fraction, ct), ct);
                return WriteOffice(file, ".xlsx", s => OfficeWriters.WriteWorkbook(s, tables), context, ct);
            }
            default:
            {
                string text = await Task.Run(() => extension == ".md" ? PdfText.ToMarkdown(file, fraction, ct) : PdfText.ToPlainText(file, fraction, ct), ct);
                return SafeOutput.Write(file, context.Output, extension, null, s =>
                {
                    using var w = new StreamWriter(s, new UTF8Encoding(false), leaveOpen: true);
                    w.Write(text);
                }, _ => null, ct);
            }
        }
    }

    /// <summary>Office files are packages the Open XML SDK reads back while writing, so the temp opens read-write.</summary>
    private static string WriteOffice(string file, string extension, Action<Stream> write, OperationContext context, CancellationToken ct) =>
        SafeOutput.WriteToPath(file, context.Output, extension, null, temp =>
        {
            using var stream = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            write(stream);
        }, ValidateZip, ct);

    private static string? ValidateZip(string path)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            return zip.GetEntry("[Content_Types].xml") is null ? "the Office file is incomplete" : null;
        }
        catch (InvalidDataException) { return "the Office file is damaged"; }
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
        yield return ("PDF", "PDF to PowerPoint, Excel (tables), text and Markdown · built in");
    }
}
