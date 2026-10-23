using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Chango.Conversion.Documents;
using Chango.Conversion.Media;
using Chango.Conversion.Pdf;
using Chango.Conversion.Subtitles;
using Chango.Core;
using Chango.Tools;

namespace Chango.Tests;

/// <summary>Runs where FFmpeg is installed (winget install Gyan.FFmpeg).</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute() { if (!FfmpegProvider.Available) Skip = "FFmpeg isn't installed"; }
}

public class SubtitleTests
{
    private const string Srt = "\uFEFF1\r\n00:00:01,450 --> 00:00:03,000\r\n<i>Hello</i> there\r\n\r\n2\r\n00:00:03,500 --> 00:01:02,005\r\nGeneral Kenobi\r\nsecond line\r\n\r\n3\r\n00:01:03,000 --> 00:01:04,000\r\nGeneral Kenobi\r\nsecond line\r\n";

    [Fact]
    public void Srt_becomes_vtt_and_back_without_losing_a_millisecond()
    {
        var cues = SubtitleProvider.Parse(Srt);
        Assert.Equal(3, cues.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1450), cues[0].Start);
        Assert.Equal(new TimeSpan(0, 0, 1, 2, 5), cues[1].End);

        string vtt = SubtitleProvider.ToVtt(cues);
        Assert.StartsWith("WEBVTT\n\n00:00:01.450 --> 00:00:03.000\n<i>Hello</i> there\n", vtt);

        string back = SubtitleProvider.ToSrt(SubtitleProvider.Parse(vtt));
        Assert.Equal(Srt.TrimStart('\uFEFF').Trim(), back.Trim());
    }

    [Fact]
    public void Vtt_reads_hourless_times_speakers_and_skips_notes()
    {
        const string vtt = "WEBVTT - demo\n\nNOTE written by hand\n\nintro\n00:05.000 --> 00:07.250 align:start\n<v Anna>Hi <b>there</b></v>\n";
        var cue = Assert.Single(SubtitleProvider.Parse(vtt));
        Assert.Equal(TimeSpan.FromSeconds(5), cue.Start);
        Assert.Equal("00:00:05,000 --> 00:00:07,250", SubtitleProvider.ToSrt([cue]).Split("\r\n")[1]);
        Assert.Contains("Anna: Hi <b>there</b>", SubtitleProvider.ToSrt([cue]));
        Assert.Equal("Anna: Hi there\r\n", SubtitleProvider.ToTranscript([cue]));
    }

    [Fact]
    public void A_transcript_drops_tags_and_repeated_captions()
    {
        Assert.Equal("Hello there\r\nGeneral Kenobi second line\r\n", SubtitleProvider.ToTranscript(SubtitleProvider.Parse(Srt)));
    }

    [Fact]
    public async Task The_wheel_offers_the_other_format_and_a_transcript()
    {
        using var t = new TempFolder();
        string srt = t.File("talk.srt");
        File.WriteAllText(srt, Srt);
        var ids = new SubtitleProvider().GetOperations(new FileSet([srt]), OperationMode.Convert).Select(o => o.Id).ToList();
        Assert.Equal(["convert.subtitles.vtt", "convert.subtitles.txt"], ids);

        var o = Assert.Single(await Runner.Run(new SubtitleProvider(), "convert.subtitles.vtt", srt));
        Assert.Equal(t.File("talk.vtt"), o.Output);
        Assert.StartsWith("WEBVTT", File.ReadAllText(o.Output!));
    }
}

public class MarkupTests
{
    [Fact]
    public async Task Markdown_becomes_html_and_text_without_office()
    {
        using var t = new TempFolder();
        string md = t.File("notes.md");
        File.WriteAllText(md, "# Plan\n\nShip **Chango** soon.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n");

        var html = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.html", md));
        string page = File.ReadAllText(html.Output!);
        Assert.Contains("<h1 id=\"plan\">Plan</h1>", page);
        Assert.Contains("<strong>Chango</strong>", page);
        Assert.Contains("<table>", page);

        var txt = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.txt", md));
        string text = File.ReadAllText(txt.Output!);
        Assert.Contains("Ship Chango soon.", text);
        Assert.DoesNotContain("**", text);
    }

    [Fact]
    public void Plain_text_keeps_its_paragraphs_and_is_escaped()
    {
        string body = Markup.ToHtmlBody("a < b\nsecond line\n\nnew paragraph", markdown: false);
        Assert.Equal("<p>a &lt; b<br>\nsecond line</p>\n<p>new paragraph</p>\n", body);
    }
}

public class DocxToMarkdownTests
{
    [Fact]
    public void Headings_emphasis_links_and_tables_come_through()
    {
        using var t = new TempFolder();
        string path = t.File("doc.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(new Style(new StyleName { Val = "heading 1" }) { StyleId = "Heading1", Type = StyleValues.Paragraph });
            string link = main.AddHyperlinkRelationship(new Uri("https://example.com/a b"), true).Id;
            main.Document = new Document(new Body(
                new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text("Quarterly report"))),
                new Paragraph(
                    new Run(new Text("Sales were ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new RunProperties(new Bold()), new Text("up 5% ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new Text("(see ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Hyperlink(new Run(new Text("details"))) { Id = link },
                    new Run(new Text(") and *stars*."))),
                new Paragraph(),
                new Table(
                    new TableRow(new TableCell(new Paragraph(new Run(new Text("Region")))), new TableCell(new Paragraph(new Run(new Text("Total"))))),
                    new TableRow(new TableCell(new Paragraph(new Run(new Text("North")))), new TableCell(new Paragraph(new Run(new Text("1|2")))))),
                new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Consolas" }), new Text("var x = 1;")))));
        }

        string md = DocxToMarkdown.Convert(path);
        Assert.Equal(string.Join(Environment.NewLine,
            "# Quarterly report",
            "",
            "Sales were **up 5%** (see [details](https://example.com/a%20b)) and \\*stars\\*.",
            "",
            "| Region | Total |",
            "| --- | --- |",
            "| North | 1\\|2 |",
            "",
            "```",
            "var x = 1;",
            "```",
            ""), md);
    }
}

public class PdfContentTests
{
    [Theory]
    [InlineData("1,234.50", 1234.5)]
    [InlineData("-7", -7.0)]
    [InlineData("(12)", -12.0)]
    [InlineData("50%", 0.5)]
    public void Numbers_in_tables_become_numbers(string text, double value) => Assert.Equal(value, PdfText.AsNumber(text));

    [Theory]
    [InlineData("12 apples")]
    [InlineData("2024-01-05")]
    [InlineData("1,23")]
    public void Other_text_stays_text(string text) => Assert.Null(PdfText.AsNumber(text));

    [Fact]
    public void Column_names_follow_excel()
    {
        Assert.Equal(["A", "Z", "AA", "AZ", "BA", "ZZ", "AAA"], new[] { 0, 25, 26, 51, 52, 701, 702 }.Select(OfficeWriters.ColumnName));
    }

    [Fact]
    public void Pdf_gets_slides_tables_text_and_markdown_options()
    {
        var ids = new PdfContentProvider().GetOperations(new FileSet([@"C:\x\a.pdf"]), OperationMode.Convert).Select(o => o.Id);
        Assert.Equal(["convert.pdf.pptx", "convert.pdf.xlsx", "convert.pdf.txt", "convert.pdf.md"], ids);
        Assert.Empty(new PdfContentProvider().GetOperations(new FileSet([@"C:\x\a.pdf", @"C:\x\b.docx"]), OperationMode.Convert));
    }

    /// <summary>A real PDF with a heading, a paragraph, a list and a table, made by Word from Markdown.</summary>
    private static async Task<string> MakePdf(TempFolder t)
    {
        string md = t.File("source.md");
        File.WriteAllText(md, "# Annual summary\n\nThis paragraph explains the numbers that follow in the table below.\n\n- First point\n- Second point\n\n| Region | Units | Revenue |\n|---|---|---|\n| North | 120 | 1,500.25 |\n| South | 80 | 990.00 |\n");
        var pdf = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.pdf", md));
        Assert.True(pdf.Status == OutcomeStatus.Succeeded, pdf.Message);
        return pdf.Output!;
    }

    [OfficeFact("Word.Application")]
    public async Task Pdf_text_and_markdown_keep_the_structure()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t);

        var md = Assert.Single(await Runner.Run(new PdfContentProvider(), "convert.pdf.md", pdf));
        Assert.True(md.Status == OutcomeStatus.Succeeded, md.Message);
        string markdown = File.ReadAllText(md.Output!);
        Assert.Contains("# Annual summary", markdown);
        Assert.Contains("- First point", markdown);
        Assert.Contains("This paragraph explains the numbers that follow in the table below.", markdown);
        Assert.Contains("| Region | Units | Revenue |", markdown);
        Assert.Contains("| North | 120 | 1,500.25 |", markdown);

        var txt = Assert.Single(await Runner.Run(new PdfContentProvider(), "convert.pdf.txt", pdf));
        Assert.Contains("Annual summary", File.ReadAllText(txt.Output!));
    }

    [OfficeFact("Excel.Application")]
    public async Task Pdf_tables_become_spreadsheet_cells_excel_can_read()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t);
        var xlsx = Assert.Single(await Runner.Run(new PdfContentProvider(), "convert.pdf.xlsx", pdf));
        Assert.True(xlsx.Status == OutcomeStatus.Succeeded, xlsx.Message);

        // Excel itself opens it (proves the file is valid) and the table's rows come out as separate cells.
        var csv = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.csv", xlsx.Output!));
        Assert.True(csv.Status == OutcomeStatus.Succeeded, csv.Message);
        var lines = File.ReadAllLines(csv.Output!).Select(l => l.TrimStart('\uFEFF')).ToList();
        Assert.Contains(lines, l => l.StartsWith("North,120,1500.25"));
        Assert.Contains(lines, l => l.StartsWith("South,80,990"));
    }

    [OfficeFact("PowerPoint.Application")]
    public async Task Pdf_becomes_a_deck_powerpoint_opens()
    {
        using var t = new TempFolder();
        string pdf = await MakePdf(t);
        var pptx = Assert.Single(await Runner.Run(new PdfContentProvider(), "convert.pdf.pptx", pdf));
        Assert.True(pptx.Status == OutcomeStatus.Succeeded, pptx.Message);
        Assert.Equal(t.File("source.pptx"), pptx.Output);

        // PowerPoint opening it and printing it to PDF proves the deck is valid.
        var back = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.pdf", pptx.Output!));
        Assert.True(back.Status == OutcomeStatus.Succeeded, back.Message);
    }
}

public class OfficeFormatTests
{
    [Fact]
    public void Tsv_fields_are_quoted_only_when_they_must_be()
    {
        string excel = "Name\tCity\r\n\"Zoë\"\t\"Paris, FR\"\r\n\"say \"\"hi\"\"\"\t\"two\nlines\"\r\n";
        Assert.Equal("Name\tCity\r\nZoë\tParis, FR\r\n\"say \"\"hi\"\"\"\t\"two\nlines\"\r\n", DocumentEngines.CleanTsv(excel));
    }

    [OfficeFact("Word.Application")]
    public async Task Markdown_becomes_a_word_document_and_back()
    {
        using var t = new TempFolder();
        string md = t.File("plan.md");
        File.WriteAllText(md, "# Launch plan\n\n## Steps\n\n1. Build\n2. Test\n\n- **Bold** idea\n- *Italic* idea\n\n| Who | What |\n|---|---|\n| Ana | Docs |\n");
        var docx = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.docx", md));
        Assert.True(docx.Status == OutcomeStatus.Succeeded, docx.Message);

        var back = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.md", docx.Output!));
        Assert.True(back.Status == OutcomeStatus.Succeeded, back.Message);
        string text = File.ReadAllText(back.Output!);
        Assert.Contains("# Launch plan", text);
        Assert.Contains("## Steps", text);
        Assert.Contains("1. Build", text);
        Assert.Contains("2. Test", text);
        Assert.Contains("- **Bold** idea", text);
        Assert.Contains("- *Italic* idea", text);
        Assert.Contains("| Who | What |", text);
    }

    [OfficeFact("Word.Application")]
    public async Task Word_saves_text_html_rtf_and_odt()
    {
        using var t = new TempFolder();
        string rtf = t.File("letter.rtf");
        File.WriteAllText(rtf, @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs28 Hello from Chango \u8364? caf\'e9.\par}");
        var txt = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.txt", rtf));
        Assert.Contains("Hello from Chango € café.", File.ReadAllText(txt.Output!));
        var html = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.html", rtf));
        // Word wraps lines in its HTML source, so compare the words.
        Assert.Contains("Hello from Chango", System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(html.Output!), @"\s+", " "));
        var odt = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.odt", rtf));
        using (var zip = ZipFile.OpenRead(odt.Output!)) Assert.NotNull(zip.GetEntry("content.xml"));
    }

    [OfficeFact("Excel.Application")]
    public async Task Csv_becomes_a_workbook_and_tab_separated_text()
    {
        using var t = new TempFolder();
        string csv = t.File("data.csv");
        File.WriteAllText(csv, "Name,City,Amount\nZoë,\"Paris, FR\",12.5\nLi,Seoul,7\n", new System.Text.UTF8Encoding(false));

        var xlsx = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.xlsx", csv));
        Assert.True(xlsx.Status == OutcomeStatus.Succeeded, xlsx.Message);
        var tsv = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.tsv", xlsx.Output!));
        Assert.True(tsv.Status == OutcomeStatus.Succeeded, tsv.Message);
        var lines = File.ReadAllLines(tsv.Output!, System.Text.Encoding.UTF8).Select(l => l.TrimStart('\uFEFF')).ToList();
        Assert.Equal("Name\tCity\tAmount", lines[0]);
        Assert.Equal("Zoë\tParis, FR\t12.5", lines[1]);

        var ods = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.ods", csv));
        using var zip = ZipFile.OpenRead(ods.Output!);
        Assert.NotNull(zip.GetEntry("content.xml"));
    }

    [OfficeFact("PowerPoint.Application")]
    public async Task Slides_become_one_image_per_slide()
    {
        using var t = new TempFolder();
        string src = t.File("deck.pptx");
        await Chango.Conversion.StaWorker.RunAsync(() =>
        {
            dynamic pp = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
            dynamic deck = pp.Presentations.Add(0);
            deck.Slides.Add(1, 1).Shapes[1].TextFrame.TextRange.Text = "One";
            deck.Slides.Add(2, 1).Shapes[1].TextFrame.TextRange.Text = "Two";
            deck.SaveAs(src, 24);
            deck.Close();
            if (pp.Presentations.Count == 0) pp.Quit();
        });
        var png = Assert.Single(await Runner.Run(new DocumentProvider(), "convert.document.png", src));
        Assert.True(png.Status == OutcomeStatus.Succeeded, png.Message);
        Assert.Equal(t.File("deck (PNG)"), png.Output);
        Assert.Equal(2, Directory.GetFiles(png.Output!, "*.png").Length);
    }
}

public class FfmpegTests
{
    [Fact]
    public void Ffmpeg_options_never_duplicate_windows_own()
    {
        if (!FfmpegProvider.Available) return;
        var mp4 = new FfmpegProvider().GetOperations(new FileSet([@"C:\x\clip.mp4"]), OperationMode.Convert).ToList();
        Assert.Equal(["MKV", "WEBM", "MOV", "AVI", "OGG", "OPUS", "AIFF"], mp4.Select(o => o.Label));
        Assert.All(mp4, o => Assert.Equal("media.more", o.Group?.Id));

        var flv = new FfmpegProvider().GetOperations(new FileSet([@"C:\x\clip.flv"]), OperationMode.Convert).Select(o => o.Label).ToList();
        Assert.Contains("MP4", flv);
        Assert.Contains("MP3", flv);

        var ogg = new FfmpegProvider().GetOperations(new FileSet([@"C:\x\song.ogg"]), OperationMode.Convert).Select(o => o.Label).ToList();
        Assert.Equal(["MP3", "M4A", "WAV", "FLAC", "OPUS", "AIFF"], ogg);
    }

    [FfmpegFact]
    public async Task Video_becomes_mkv_webm_and_its_audio_ogg()
    {
        using var t = new TempFolder();
        string clip = t.File("clip.mp4");
        var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FfmpegProvider.ExecutablePath!,
            ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=15", "-f", "lavfi", "-i", "sine=duration=2",
             "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", clip]) { CreateNoWindow = true, UseShellExecute = false })!;
        await make.WaitForExitAsync();
        Assert.True(File.Exists(clip));

        foreach (var (id, name) in new[] { ("convert.ffmpeg.mkv", "clip.mkv"), ("convert.ffmpeg.webm", "clip.webm"), ("convert.ffmpeg.ogg", "clip.ogg") })
        {
            var o = Assert.Single(await Runner.Run(new FfmpegProvider(), id, clip));
            Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
            Assert.Equal(t.File(name), o.Output);
            Assert.True(new FileInfo(o.Output!).Length > 1000);
        }
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));
    }
}

public class ArchiveFormatTests
{
    [Fact]
    public async Task Folders_go_into_7z_and_tar_gz_and_come_back_out()
    {
        using var t = new TempFolder();
        string folder = Directory.CreateDirectory(t.File("docs")).FullName;
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "hello");
        string file = t.File("notes.txt");
        File.WriteAllText(file, "notes");

        foreach (var (id, name) in new[] { ("action.archive.7z", "Archive.7z"), ("action.archive.tar.gz", "Archive.tar.gz") })
        {
            var packed = Assert.Single(await Runner.Run(new ArchiveProvider(), id, folder, file), o => o.Output is not null);
            Assert.True(packed.Status == OutcomeStatus.Succeeded, packed.Message);
            Assert.Equal(t.File(name), packed.Output);

            var extracted = Assert.Single(await Runner.Run(new ArchiveProvider(), "tool.archive.extract", packed.Output!));
            Assert.True(extracted.Status == OutcomeStatus.Succeeded, extracted.Message);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(extracted.Output!, "docs", "readme.txt")));
            Assert.Equal("notes", File.ReadAllText(Path.Combine(extracted.Output!, "notes.txt")));
            Directory.Delete(extracted.Output!, true);
        }
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));
    }

    [Fact]
    public async Task A_damaged_7z_fails_with_tars_reason()
    {
        using var t = new TempFolder();
        string bad = t.File("broken.7z");
        File.WriteAllBytes(bad, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0, 4, 1, 2, 3]); // a 7z signature, then nothing usable
        var o = Assert.Single(await Runner.Run(new ArchiveProvider(), "tool.archive.extract", bad));
        Assert.Equal(OutcomeStatus.Failed, o.Status);
        Assert.False(string.IsNullOrWhiteSpace(o.Message));
        Assert.False(Directory.Exists(t.File("broken")));
    }
}
