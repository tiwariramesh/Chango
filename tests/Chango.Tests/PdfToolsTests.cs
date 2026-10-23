using Chango.Conversion.Pdf;
using Chango.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Chango.Tests;

public class PdfToolsTests
{
    private static int PageCount(string pdf) { using var d = PdfReader.Open(pdf, PdfDocumentOpenMode.Import); return d.PageCount; }

    /// <summary>A PDF made by Chango itself from images: one page per image, photos embedded as JPEG.</summary>
    private static async Task<string> MakePdf(TempFolder t, string name, int pages, int width = 400, int height = 300)
    {
        var images = Enumerable.Range(1, pages).Select(i => t.Image($"{name}-{i}.jpg", width, height)).ToArray();
        var outcomes = await Runner.Run(new PdfProvider(), pages > 1 ? "tool.image.pdf-combine" : "convert.image.pdf", images);
        string made = outcomes.First(o => o.Output is not null).Output!;
        string pdf = t.File($"{name}.pdf");
        File.Move(made, pdf);
        return pdf;
    }

    /// <summary>Windows' own PDF engine must be able to open and render the result.</summary>
    private static async Task AssertRenders(string pdf)
    {
        var png = Assert.Single(await Runner.Run(new PdfProvider(), "convert.pdf.png", pdf));
        Assert.True(png.Status == OutcomeStatus.Succeeded, png.Message);
    }

    [Fact]
    public void Offers_the_right_tools()
    {
        var one = new PdfToolsProvider().GetOperations(new FileSet(["a.pdf"]), OperationMode.Tools).Select(o => o.Id).ToList();
        Assert.DoesNotContain("tool.pdf.merge", one);
        Assert.Contains("tool.pdf.split", one);
        Assert.Contains("tool.pdf.compress", one);
        Assert.Contains("tool.pdf.rotate.right", one);
        Assert.Contains("tool.pdf.merge", new PdfToolsProvider().GetOperations(new FileSet(["a.pdf", "b.pdf"]), OperationMode.Tools).Select(o => o.Id));
        Assert.Empty(new PdfToolsProvider().GetOperations(new FileSet(["a.pdf", "b.png"]), OperationMode.Tools));
        Assert.Empty(new PdfToolsProvider().GetOperations(new FileSet(["a.pdf"]), OperationMode.Convert));
    }

    [Fact]
    public async Task Merges_in_the_order_picked()
    {
        using var t = new TempFolder();
        string a = await MakePdf(t, "a", 2), b = await MakePdf(t, "b", 3);
        var outcomes = await Runner.Run(new PdfToolsProvider(), "tool.pdf.merge", a, b);
        Assert.All(outcomes, o => Assert.Equal(OutcomeStatus.Succeeded, o.Status));
        string merged = Assert.Single(outcomes, o => o.Output is not null).Output!;
        Assert.Equal(t.File("a (merged).pdf"), merged);
        Assert.Equal(5, PageCount(merged));
        Assert.Equal(2, PageCount(a)); // originals untouched
        await AssertRenders(merged);
    }

    [Fact]
    public async Task Splits_into_a_folder_of_one_page_pdfs()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t, "report", 3);
        var o = Assert.Single(await Runner.Run(new PdfToolsProvider(), "tool.pdf.split", pdf));
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("report (pages)"), o.Output);
        var pages = Directory.GetFiles(o.Output!).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(["Page 1.pdf", "Page 2.pdf", "Page 3.pdf"], pages);
        Assert.All(Directory.GetFiles(o.Output!), p => Assert.Equal(1, PageCount(p)));
    }

    [Fact]
    public async Task A_one_page_pdf_is_not_split()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t, "single", 1);
        var o = Assert.Single(await Runner.Run(new PdfToolsProvider(), "tool.pdf.split", pdf));
        Assert.Equal(OutcomeStatus.Skipped, o.Status);
    }

    [Fact]
    public async Task Rotates_every_page()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t, "scan", 2);
        var o = Assert.Single(await Runner.Run(new PdfToolsProvider(), "tool.pdf.rotate.left", pdf));
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("scan (rotated).pdf"), o.Output);
        using (var d = PdfReader.Open(o.Output!, PdfDocumentOpenMode.Import))
            Assert.All(d.Pages.Cast<PdfPage>(), p => Assert.Equal(270, p.Rotate));
        await AssertRenders(o.Output!);
    }

    [Fact]
    public async Task Compresses_big_photos_and_keeps_every_page()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t, "photos", 2, 3200, 2400);
        long before = new FileInfo(pdf).Length;
        var o = Assert.Single(await Runner.Run(new PdfToolsProvider(), "tool.pdf.compress", pdf));
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("photos (compressed).pdf"), o.Output);
        Assert.True(new FileInfo(o.Output!).Length < before, $"{new FileInfo(o.Output!).Length} >= {before}");
        Assert.Equal(2, PageCount(o.Output!));
        await AssertRenders(o.Output!);
    }

    [Fact]
    public async Task Says_so_when_a_pdf_is_password_protected()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t, "secret", 2);
        string locked = t.File("locked.pdf");
        using (var d = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify))
        {
            d.SecuritySettings.UserPassword = "pw";
            d.SecuritySettings.OwnerPassword = "owner";
            d.Save(locked);
        }
        var o = Assert.Single(await Runner.Run(new PdfToolsProvider(), "tool.pdf.split", locked));
        Assert.Equal(OutcomeStatus.Failed, o.Status);
        Assert.Contains("password", o.Message, StringComparison.OrdinalIgnoreCase);
    }
}
