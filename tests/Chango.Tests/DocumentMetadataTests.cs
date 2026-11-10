using System.Diagnostics;
using Chango.Conversion.Documents;
using Chango.Conversion.Pdf;
using Chango.Core;
using DocumentFormat.OpenXml.Packaging;
using NetVips;
using VImage = NetVips.Image;

namespace Chango.Tests;

/// <summary>Titles, authors and dates travel from the original into PDFs and Office files Chango makes.</summary>
public class DocumentMetadataTests
{
    private static readonly DateTime Created = new(2018, 3, 14, 9, 26, 53, DateTimeKind.Local);

    private static string Pdf(TempFolder t, string name, int pages = 1)
    {
        string path = t.File(name);
        var doc = new PdfSharp.Pdf.PdfDocument();
        doc.Info.Title = "Annual report";
        doc.Info.Author = "Rāmesh Tiwari"; // non-ASCII on purpose
        doc.Info.Keywords = "finance; 2018";
        doc.Info.CreationDate = Created;
        for (int i = 0; i < pages; i++) doc.AddPage();
        doc.Save(path);
        return path;
    }

    private static FileOutcome[] Run(IOperationProvider provider, OperationMode mode, string id, params string[] files)
    {
        var set = new FileSet(files);
        var op = provider.GetOperations(set, mode).Single(d => d.Id == id);
        var c = new Collector();
        provider.ExecuteAsync(op, set, Ctx.Default(), c, CancellationToken.None).GetAwaiter().GetResult();
        Assert.All(c.Outcomes, o => Assert.True(o.Status == OutcomeStatus.Succeeded, $"{id}: {o.Status} {o.Message}"));
        return [.. c.Outcomes];
    }

    [Fact]
    public void Document_details_are_read_from_a_pdf()
    {
        using var t = new TempFolder();
        var info = DocInfo.FromPdf(Pdf(t, "a.pdf"));
        Assert.Equal("Annual report", info.Title);
        Assert.Equal("Rāmesh Tiwari", info.Author);
        Assert.Equal(Created, info.Created!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Merging_keeps_the_first_documents_details()
    {
        using var t = new TempFolder();
        var outcomes = Run(new PdfToolsProvider(), OperationMode.Tools, "tool.pdf.merge", Pdf(t, "a.pdf"), Pdf(t, "b.pdf", 2));
        var merged = DocInfo.FromPdf(outcomes.First(o => o.Output is not null).Output!);
        Assert.Equal("Annual report", merged.Title);
        Assert.Equal("Rāmesh Tiwari", merged.Author);
        Assert.Equal("finance; 2018", merged.Keywords);
        Assert.Equal(Created, merged.Created!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Split_pages_keep_the_documents_details_and_dates()
    {
        using var t = new TempFolder();
        string src = Pdf(t, "a.pdf", 3);
        var modified = new DateTime(2018, 3, 15, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(src, modified);
        var folder = Run(new PdfToolsProvider(), OperationMode.Tools, "tool.pdf.split", src)[0].Output!;
        var pages = Directory.GetFiles(folder);
        Assert.Equal(3, pages.Length);
        Assert.All(pages, p =>
        {
            Assert.Equal("Rāmesh Tiwari", DocInfo.FromPdf(p).Author);
            Assert.Equal(modified, File.GetLastWriteTimeUtc(p));
        });
    }

    [Fact]
    public void A_pdf_made_from_a_photo_carries_the_photographer_and_date_taken()
    {
        using var t = new TempFolder();
        string photo = t.File("trip.jpg");
        using (var x = VImage.Black(64, 48, bands: 3).Copy(interpretation: Enums.Interpretation.Srgb))
        using (var tagged = x.Mutate(m =>
        {
            m.Set(GValue.GStrType, "exif-ifd0-Artist", "Ramesh");
            m.Set(GValue.GStrType, "exif-ifd2-DateTimeOriginal", "2019:05:04 10:30:00");
        }))
            tagged.Jpegsave(photo, keep: Enums.ForeignKeep.All);
        var pdf = Run(new PdfProvider(), OperationMode.Convert, "convert.image.pdf", photo)[0].Output!;
        var info = DocInfo.FromPdf(pdf);
        Assert.Equal("Ramesh", info.Author);
        Assert.Equal("trip", info.Title);
        Assert.Equal(new DateTime(2019, 5, 4, 10, 30, 0), info.Created!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_powerpoint_made_from_a_pdf_has_its_title_and_author()
    {
        using var t = new TempFolder();
        var pptx = Run(new PdfContentProvider(), OperationMode.Convert, "convert.pdf.pptx", Pdf(t, "a.pdf"))[0].Output!;
        using var deck = PresentationDocument.Open(pptx, false);
        Assert.Equal("Annual report", deck.PackageProperties.Title);
        Assert.Equal("Rāmesh Tiwari", deck.PackageProperties.Creator);
    }

    [OfficeFact("Word.Application")]
    public void A_batch_of_word_documents_shares_one_word()
    {
        using var t = new TempFolder();
        var docs = Enumerable.Range(1, 5).Select(i => t.File($"letter{i}.rtf")).ToList();
        foreach (var d in docs) File.WriteAllText(d, @"{\rtf1\ansi Dear reader,\par this is letter " + Path.GetFileName(d) + @".}");
        var watch = Stopwatch.StartNew();
        var outcomes = Run(new DocumentProvider(), OperationMode.Convert, "convert.document.pdf", [.. docs]);
        watch.Stop();
        Assert.Equal(5, outcomes.Length);
        Assert.All(outcomes, o => Assert.Null(PdfProvider.ValidatePdf(o.Output!)));
        Console.WriteLine($"5 documents to PDF through one Word: {watch.Elapsed.TotalSeconds:0.0} s");
    }
}
