using Flipr.Conversion;
using Flipr.Conversion.Documents;
using Flipr.Conversion.Imaging;
using Flipr.Conversion.Pdf;
using Flipr.Core;

namespace Flipr.Tests;

/// <summary>Runs only where Microsoft Office is installed (not on CI): the conversions go through the real Office apps.</summary>
public sealed class OfficeFactAttribute : FactAttribute
{
    public OfficeFactAttribute(string progId)
    {
        if (Type.GetTypeFromProgID(progId) is null) Skip = $"{progId} isn't installed";
    }
}

public class DocumentTests
{
    private static async Task<FileOutcome> Run(string id, string file)
    {
        var provider = new DocumentProvider();
        var op = provider.GetOperations(new FileSet([file]), OperationMode.Convert).Single(d => d.Id == id);
        var c = new Collector();
        await provider.ExecuteAsync(op, new FileSet([file]), Ctx.Default(), c, CancellationToken.None);
        return Assert.Single(c.Outcomes);
    }

    [Fact]
    public void Only_documents_get_document_options()
    {
        Assert.Empty(new DocumentProvider().GetOperations(new FileSet([@"C:\x\photo.jpg"]), OperationMode.Convert));
        Assert.Empty(new DocumentProvider().GetOperations(new FileSet([@"C:\x\report.docx"]), OperationMode.Tools));
        Assert.Equal(DocumentKind.Drawing, DocumentEngines.KindOf("logo.CDR"));
    }

    [OfficeFact("Word.Application")]
    public async Task Rtf_becomes_a_pdf_and_a_docx_through_word()
    {
        using var t = new TempFolder();
        string src = t.File("letter.rtf");
        File.WriteAllText(src, @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs28 Hello from Flipr.\par}");
        string hash = SafeOutput.Sha256(src);

        var pdf = await Run("convert.document.pdf", src);
        Assert.Equal(OutcomeStatus.Succeeded, pdf.Status);
        Assert.Equal(t.File("letter.pdf"), pdf.Output);
        Assert.Null(PdfProvider.ValidatePdf(pdf.Output!));

        var docx = await Run("convert.document.docx", src);
        Assert.Equal(t.File("letter.docx"), docx.Output);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(docx.Output!)) Assert.NotNull(zip.GetEntry("word/document.xml"));
        Assert.Equal(hash, SafeOutput.Sha256(src));
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));
    }

    [OfficeFact("Word.Application")]
    public async Task Pdf_becomes_a_word_document_even_with_a_long_name()
    {
        using var t = new TempFolder();
        string rtf = t.File("letter.rtf");
        File.WriteAllText(rtf, @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs28 Hello from Flipr.\par}");
        string pdf = (await Run("convert.document.pdf", rtf)).Output!;
        string longName = t.File(new string('x', 120) + " & a very long quarterly report name.pdf");
        File.Move(pdf, longName);

        var docx = await Run("convert.document.docx", longName);
        Assert.True(docx.Status == OutcomeStatus.Succeeded, docx.Message);
        Assert.Equal(Path.ChangeExtension(longName, ".docx"), docx.Output);
        using var zip = System.IO.Compression.ZipFile.OpenRead(docx.Output!);
        Assert.NotNull(zip.GetEntry("word/document.xml"));
    }

    [OfficeFact("Excel.Application")]
    public async Task Xlsx_becomes_a_pdf_through_excel()
    {
        using var t = new TempFolder();
        string src = t.File("sheet.xlsx");
        await StaWorker.RunAsync(() =>
        {
            dynamic xl = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application")!)!;
            xl.DisplayAlerts = false;
            dynamic book = xl.Workbooks.Add();
            book.Worksheets[1].Range("A1").Value = "Flipr";
            book.SaveAs(src, 51);
            book.Close(false);
            xl.Quit();
        });
        var pdf = await Run("convert.document.pdf", src);
        Assert.Equal(OutcomeStatus.Succeeded, pdf.Status);
        Assert.Null(PdfProvider.ValidatePdf(pdf.Output!));
    }

    [OfficeFact("PowerPoint.Application")]
    public async Task Pptx_becomes_a_pdf_through_powerpoint()
    {
        using var t = new TempFolder();
        string src = t.File("deck.pptx");
        await StaWorker.RunAsync(() =>
        {
            dynamic pp = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
            dynamic deck = pp.Presentations.Add(0); // no window
            deck.Slides.Add(1, 1).Shapes[1].TextFrame.TextRange.Text = "Flipr";
            deck.SaveAs(src, 24);
            deck.Close();
            if (pp.Presentations.Count == 0) pp.Quit();
        });
        var pdf = await Run("convert.document.pdf", src);
        Assert.Equal(OutcomeStatus.Succeeded, pdf.Status);
        Assert.Null(PdfProvider.ValidatePdf(pdf.Output!));
    }
}

/// <summary>Runs where FliprCdr.exe has been built (build/cdr/build-cdr.ps1); package.ps1 builds it before the tests.</summary>
public sealed class CdrReaderFactAttribute : FactAttribute
{
    public static string Reader => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\artifacts\cdr\FliprCdr.exe"));
    public CdrReaderFactAttribute() { if (!File.Exists(Reader)) Skip = "FliprCdr.exe isn't built"; }
}

/// <summary>
/// CorelDRAW, read by Flipr's own reader (no Inkscape, no LibreOffice), on LibreOffice's public test drawing and,
/// when FLIPR_CDR_SAMPLE points at one, a real-world download.
/// </summary>
public class DrawingTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "fdo65220-2.cdr");
    private static string? Sample => Environment.GetEnvironmentVariable("FLIPR_CDR_SAMPLE") is { Length: > 0 } p
        ? File.Exists(p) ? p : throw new FileNotFoundException("FLIPR_CDR_SAMPLE points to a missing file", p)
        : null;

    public DrawingTests() => CorelDraw.ReaderPath = CdrReaderFactAttribute.Reader;

    private static async Task<FileOutcome> Convert(string src, string id)
    {
        var provider = new DocumentProvider();
        var op = provider.GetOperations(new FileSet([src]), OperationMode.Convert).Single(d => d.Id == id);
        var c = new Collector();
        await provider.ExecuteAsync(op, new FileSet([src]), Ctx.Default(), c, CancellationToken.None);
        return Assert.Single(c.Outcomes);
    }

    [CdrReaderFact]
    public void Cdr_offers_every_drawing_format_with_nothing_installed()
    {
        var labels = new DocumentProvider().GetOperations(new FileSet([@"C:\x\logo.cdr"]), OperationMode.Convert).Select(o => o.Label);
        Assert.Equal(["PDF", "SVG", "PNG", "JPG", "WEBP", "TIFF", "BMP"], labels);
        Assert.NotEmpty(new DocumentProvider().GetOperations(new FileSet([@"C:\x\exchange.CMX"]), OperationMode.Convert));
    }

    [CdrReaderFact]
    public async Task Cdr_converts_to_every_format_even_with_a_long_name()
    {
        using var t = new TempFolder();
        foreach (var (id, ext) in new[] { ("convert.drawing.svg", ".svg"), ("convert.drawing.png", ".png"), ("convert.drawing.jpg", ".jpg"),
            ("convert.drawing.webp", ".webp"), ("convert.drawing.tiff", ".tiff"), ("convert.drawing.bmp", ".bmp"), ("convert.drawing.pdf", ".pdf") })
        {
            // A long name like the ones design sites give their downloads (with "&"): paths must stay under 260 characters.
            string src = t.File($"Kitchen Wall Art Poster - Typography Vector Design & Quote For Free In Coreldraw Design Download ({ext.TrimStart('.')}) - {new string('x', 40)}.cdr");
            File.Copy(Fixture, src);
            var outcome = await Convert(src, id);
            Assert.True(outcome.Status == OutcomeStatus.Succeeded, $"{id}: {outcome.Message}");
            Assert.Equal(Path.ChangeExtension(src, ext), outcome.Output);
            if (ext == ".svg") Assert.Contains("<svg", File.ReadAllText(outcome.Output!));
            else if (ext == ".pdf") Assert.Null(PdfProvider.ValidatePdf(outcome.Output!));
            else
            {
                (int W, int H) size = default;
                await StaWorker.RunAsync(() => { var b = WicImageCodec.Load(outcome.Output!); size = (b.PixelWidth, b.PixelHeight); });
                Assert.True(size.W >= 1000 || size.H >= 1000, $"{ext} is only {size.W}×{size.H}"); // drawn big enough to be sharp
            }
        }
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));
    }

    [CdrReaderFact]
    public async Task A_broken_or_fake_cdr_fails_with_a_clear_message()
    {
        using var t = new TempFolder();
        string fake = t.File("not really.cdr");
        File.WriteAllText(fake, "this is not a CorelDRAW drawing");
        var outcome = await Convert(fake, "convert.drawing.png");
        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        Assert.Contains("CorelDRAW", outcome.Message);
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));
    }

    [CdrReaderFact]
    public async Task A_real_world_download_converts()
    {
        if (Sample is not { } sample) return;
        using var t = new TempFolder();
        string src = t.File(Path.GetFileName(sample));
        File.Copy(sample, src);
        var outcome = await Convert(src, "convert.drawing.png");
        Assert.True(outcome.Status == OutcomeStatus.Succeeded, outcome.Message);
    }
}
