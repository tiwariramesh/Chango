using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using OrbitDrop.Conversion.Media;
using OrbitDrop.Conversion.Pdf;
using OrbitDrop.Core;
using OrbitDrop.Tools;

namespace OrbitDrop.Tests;

internal static class Runner
{
    public static async Task<List<FileOutcome>> Run(IOperationProvider provider, string id, params string[] items)
    {
        var files = new FileSet(items);
        var op = Enum.GetValues<OperationMode>().SelectMany(m => provider.GetOperations(files, m)).Single(d => d.Id == id);
        var c = new Collector();
        await provider.ExecuteAsync(op, files, Ctx.Default(), c, CancellationToken.None);
        return c.Outcomes;
    }
}

public class PdfTests
{
    private static int Pages(string pdf) =>
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(pdf, Encoding.Latin1), @"/Type /Page\b").Count;

    [Fact]
    public async Task Image_becomes_a_valid_pdf_and_renders_back()
    {
        using var t = new TempFolder();
        string src = t.Image("poster.png", 300, 200, hole: true);
        var o = Assert.Single(await Runner.Run(new PdfProvider(), "convert.image.pdf", src));
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("poster.pdf"), o.Output);
        Assert.Null(PdfProvider.ValidatePdf(o.Output!));
        Assert.Equal(1, Pages(o.Output!));

        // Windows' own PDF engine must be able to open what we wrote.
        var png = Assert.Single(await Runner.Run(new PdfProvider(), "convert.pdf.png", o.Output!));
        Assert.True(png.Status == OutcomeStatus.Succeeded, png.Message);
        Assert.Equal(t.File("poster (2).png"), png.Output); // "poster.png" is the original image: never overwritten
        Assert.EndsWith(".png", png.Output);
    }

    [Fact]
    public async Task Several_images_combine_into_one_pdf_and_pages_come_back_as_a_folder()
    {
        using var t = new TempFolder();
        string a = t.Image("a.jpg", 120, 80), b = t.Image("b.png", 80, 120), c = t.Image("c.jpg", 100, 100);
        var outcomes = await Runner.Run(new PdfProvider(), "tool.image.pdf-combine", a, b, c);
        Assert.All(outcomes, o => Assert.Equal(OutcomeStatus.Succeeded, o.Status));
        string pdf = Assert.Single(outcomes, o => o.Output is not null).Output!;
        Assert.Equal(t.File("a (combined).pdf"), pdf);
        Assert.Equal(3, Pages(pdf));

        var jpgs = Assert.Single(await Runner.Run(new PdfProvider(), "convert.pdf.jpg", pdf));
        Assert.True(jpgs.Status == OutcomeStatus.Succeeded, jpgs.Message);
        Assert.True(Directory.Exists(jpgs.Output));
        Assert.Equal(["Page 1.jpg", "Page 2.jpg", "Page 3.jpg"], Directory.GetFiles(jpgs.Output!).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Plain_jpegs_are_embedded_byte_for_byte()
    {
        using var t = new TempFolder();
        string jpg = t.Image("photo.jpg", 64, 48);
        PdfImage? page = null;
        OrbitDrop.Conversion.StaWorker.RunAsync(() => page = PdfProvider.ToPdfImage(jpg, 90)).GetAwaiter().GetResult();
        Assert.Equal(File.ReadAllBytes(jpg), page!.Jpeg);
        Assert.Equal((64, 48), (page.Width, page.Height));
    }
}

public class ArchiveTests
{
    [Fact]
    public async Task Zip_then_extract_round_trips_files_and_folders()
    {
        using var t = new TempFolder();
        string folder = Directory.CreateDirectory(t.File("docs")).FullName;
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(folder, "empty"));
        string file = t.File("notes.txt");
        File.WriteAllText(file, "notes");

        var zipped = await Runner.Run(new ArchiveProvider(), "action.zip", folder, file);
        string zip = Assert.Single(zipped, o => o.Output is not null).Output!;
        Assert.Equal(t.File("Archive.zip"), zip);

        var extracted = Assert.Single(await Runner.Run(new ArchiveProvider(), "tool.archive.extract", zip));
        Assert.Equal(t.File("Archive"), extracted.Output);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(extracted.Output!, "docs", "readme.txt")));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(extracted.Output!, "notes.txt")));
        Assert.True(Directory.Exists(Path.Combine(extracted.Output!, "docs", "empty")));
    }

    [Fact]
    public async Task A_zip_that_escapes_its_folder_is_refused()
    {
        using var t = new TempFolder();
        string evil = t.File("evil.zip");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("../escaped.txt").Open())) w.Write("gotcha");
        var o = Assert.Single(await Runner.Run(new ArchiveProvider(), "tool.archive.extract", evil));
        Assert.Equal(OutcomeStatus.Failed, o.Status);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(t.Path)!, "escaped.txt")));
        Assert.False(Directory.Exists(t.File("evil")));
    }

    [Fact]
    public async Task Tar_gz_and_gz_extract()
    {
        using var t = new TempFolder();
        string src = Directory.CreateDirectory(t.File("src")).FullName;
        File.WriteAllText(Path.Combine(src, "a.txt"), "A");
        string tgz = t.File("bundle.tar.gz");
        using (var fs = File.Create(tgz))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            TarFile.CreateFromDirectory(src, gz, includeBaseDirectory: false);
        var o = Assert.Single(await Runner.Run(new ArchiveProvider(), "tool.archive.extract", tgz));
        Assert.Equal("A", File.ReadAllText(Path.Combine(o.Output!, "a.txt")));
        Assert.EndsWith("bundle", o.Output);

        string gzFile = t.File("data.csv.gz");
        using (var fs = File.Create(gzFile))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest)) gz.Write("x,y\n1,2\n"u8);
        var csv = Assert.Single(await Runner.Run(new ArchiveProvider(), "tool.archive.extract", gzFile));
        Assert.Equal(t.File("data.csv"), csv.Output);
        Assert.Equal("x,y\n1,2\n", File.ReadAllText(csv.Output!));
    }
}

public class VideoEditingTests
{
    private static string Clip(TempFolder t, string name)
    {
        string path = t.File(name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testpattern.mp4"), path);
        return path;
    }

    [MediaFact]
    public async Task Snapshot_saves_a_png_frame()
    {
        using var t = new TempFolder();
        string clip = Clip(t, "clip.mp4");
        var o = Assert.Single(await Runner.Run(new MediaEditingProvider(), "tool.video.snapshot", clip));
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.StartsWith(t.File("clip (frame "), o.Output);
        Assert.EndsWith(".png", o.Output);
    }

    [MediaFact]
    public async Task Join_makes_one_mp4()
    {
        using var t = new TempFolder();
        string a = Clip(t, "a.mp4"), b = Clip(t, "b.mp4");
        var outcomes = await Runner.Run(new MediaEditingProvider(), "tool.video.join", a, b);
        Assert.All(outcomes, o => Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message));
        string joined = Assert.Single(outcomes, o => o.Output is not null).Output!;
        Assert.Equal(t.File("a (joined).mp4"), joined);
        Assert.Null(MediaFoundationProvider.ValidateContainer(joined, ".mp4"));
        Assert.True(new FileInfo(joined).Length > new FileInfo(a).Length / 2);
    }
}

public class GifTests
{
    [MediaFact]
    public async Task Video_becomes_a_looping_gif()
    {
        using var t = new TempFolder();
        string clip = t.File("clip.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testpattern.mp4"), clip);
        var o = Assert.Single(await Runner.Run(new MediaEditingProvider(), "convert.video.gif", clip));
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("clip.gif"), o.Output);
        byte[] gif = File.ReadAllBytes(o.Output!);
        Assert.True(gif.AsSpan().IndexOf("NETSCAPE2.0"u8) > 0);

        // Graphic Control Extensions (21 F9 04 …) carry each frame's delay in hundredths of a second.
        var delays = new List<int>();
        for (int i = 0; i + 6 < gif.Length; i++)
            if (gif[i] == 0x21 && gif[i + 1] == 0xF9 && gif[i + 2] == 0x04) delays.Add(gif[i + 4] | gif[i + 5] << 8);
        Assert.InRange(delays.Count, 10, 25); // a 2 s clip at 10 fps (a few unreadable samples may be skipped)
        Assert.All(delays, d => Assert.Equal(10, d));
    }
}
