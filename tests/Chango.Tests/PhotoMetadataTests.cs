using Chango.Conversion;
using Chango.Conversion.Imaging;
using Chango.Core;
using Chango.Tools;
using NetVips;
using VImage = NetVips.Image;

namespace Chango.Tests;

/// <summary>Photos keep their camera details, date taken and location through every conversion and tool.</summary>
public class PhotoMetadataTests
{
    private const string Taken = "2019:05:04 10:30:00";

    /// <summary>A noisy, photo-like JPEG with camera, date and GPS EXIF; <paramref name="orientation"/> 6 = stored sideways.</summary>
    private static string Photo(TempFolder t, string name, int w = 400, int h = 300, int orientation = 1)
    {
        string path = t.File(name);
        using var x = VImage.Xyz(w, h);
        using var noise = VImage.Gaussnoise(w, h, sigma: 30);
        using var r = x[0] * (255.0 / w) + noise;
        using var g = x[1] * (255.0 / h) + noise;
        using var b = (x[0] + x[1]) * 0.3 + noise;
        using var rgb = r.Bandjoin(g, b).Cast(Enums.BandFormat.Uchar).Copy(interpretation: Enums.Interpretation.Srgb);
        using var tagged = rgb.Mutate(m =>
        {
            m.Set(GValue.GStrType, "exif-ifd0-Make", "ChangoCam");
            m.Set(GValue.GStrType, "exif-ifd2-DateTimeOriginal", Taken);
            m.Set(GValue.GStrType, "exif-ifd3-GPSLatitudeRef", "N");
            m.Set(GValue.GStrType, "exif-ifd3-GPSLatitude", "27/1 42/1 30/1");
            m.Set(GValue.GStrType, "exif-ifd3-GPSLongitudeRef", "E");
            m.Set(GValue.GStrType, "exif-ifd3-GPSLongitude", "85/1 19/1 0/1");
            m.Set(GValue.GIntType, "orientation", orientation);
        });
        tagged.Jpegsave(path, q: 95, keep: Enums.ForeignKeep.All);
        return path;
    }

    private static void AssertKeepsMetadata(string output)
    {
        using var image = VImage.NewFromFile(output);
        var fields = image.GetFields();
        Assert.Contains("exif-ifd3-GPSLatitude", fields);
        Assert.Contains("exif-ifd3-GPSLongitude", fields);
        Assert.StartsWith(Taken, (string)image.Get("exif-ifd2-DateTimeOriginal"));
        Assert.StartsWith("ChangoCam", (string)image.Get("exif-ifd0-Make"));
        // Pixels are stored upright, so the result must not ask viewers to turn them again.
        if (fields.Contains("orientation")) Assert.Equal(1, (int)image.Get("orientation"));
    }

    private static (int W, int H) Dims(string path)
    {
        using var image = VImage.NewFromFile(path);
        return (image.Width, image.Height);
    }

    private static FileOutcome Run(IOperationProvider provider, OperationMode mode, string id, string file, OperationContext? context = null)
    {
        var files = new FileSet([file]);
        var op = provider.GetOperations(files, mode).Single(d => d.Id == id);
        var c = new Collector();
        provider.ExecuteAsync(op, files, context ?? Ctx.Default(), c, CancellationToken.None).GetAwaiter().GetResult();
        var outcome = Assert.Single(c.Outcomes);
        Assert.True(outcome.Status == OutcomeStatus.Succeeded, $"{id}: {outcome.Status} {outcome.Message}");
        return outcome;
    }

    [Fact]
    public void Libvips_is_available() => Assert.True(VipsCodec.Available, VipsCodec.InitError);

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public void Converting_keeps_date_taken_location_and_camera(string target)
    {
        using var t = new TempFolder();
        string src = Photo(t, "trip.jpg");
        var o = Run(new ImageConversionProvider(), OperationMode.Convert, $"convert.image.{target}", src);
        Assert.Equal(t.File($"trip.{target}"), o.Output);
        AssertKeepsMetadata(o.Output!);
    }

    [Fact(Skip = "libvips writes XMP, IPTC and the colour profile into TIFF, but not EXIF: the ExifTool step (phase 3) adds it")]
    public void Converting_to_tiff_keeps_exif()
    {
        using var t = new TempFolder();
        var o = Run(new ImageConversionProvider(), OperationMode.Convert, "convert.image.tiff", Photo(t, "trip.jpg"));
        AssertKeepsMetadata(o.Output!);
    }

    [Fact]
    public void Sideways_photos_come_out_upright_with_a_normal_orientation()
    {
        using var t = new TempFolder();
        string src = Photo(t, "side.jpg", 400, 300, orientation: 6);
        var o = Run(new ImageConversionProvider(), OperationMode.Convert, "convert.image.png", src);
        Assert.Equal((300, 400), Dims(o.Output!));
        AssertKeepsMetadata(o.Output!);
    }

    [Theory]
    [InlineData("tool.image.compress.half")]
    [InlineData("tool.image.compress.optimize")]
    [InlineData("tool.image.resize.half")]
    [InlineData("tool.image.rotate-right")]
    [InlineData("tool.image.grayscale")]
    public void Tools_keep_date_taken_location_and_camera(string id)
    {
        using var t = new TempFolder();
        string src = Photo(t, "trip.jpg", 800, 600);
        var o = Run(new ImageToolsProvider(), OperationMode.Tools, id, src);
        Assert.Equal(t.File("trip_1.jpg"), o.Output);
        AssertKeepsMetadata(o.Output!);
    }

    [Fact]
    public void Compressing_to_a_budget_fits_it_and_keeps_most_of_the_quality()
    {
        using var t = new TempFolder();
        string src = Photo(t, "big.jpg", 1600, 1200);
        long before = new FileInfo(src).Length;
        var o = Run(new ImageToolsProvider(), OperationMode.Tools, "tool.image.compress.half", src);
        long after = new FileInfo(o.Output!).Length;
        Assert.InRange(after, before / 4, before / 2); // under the budget, but not thrown far below it
        Assert.Equal((1600, 1200), Dims(o.Output!)); // met by quality alone, no scaling
    }

    [Fact]
    public void Optimize_keeps_the_format_and_saves_space()
    {
        using var t = new TempFolder();
        string src = Photo(t, "big.jpg", 1600, 1200);
        var o = Run(new ImageToolsProvider(), OperationMode.Tools, "tool.image.compress.optimize", src);
        Assert.EndsWith(".jpg", o.Output);
        Assert.True(new FileInfo(o.Output!).Length < new FileInfo(src).Length * 0.95);
    }

    [Fact]
    public void Transparent_png_becomes_a_smaller_palette_png_and_stays_transparent()
    {
        using var t = new TempFolder();
        string src = t.Image("logo.png", 600, 400, hole: true);
        var o = Run(new ImageToolsProvider(), OperationMode.Tools, "tool.image.compress.half", src);
        Assert.EndsWith(".png", o.Output);
        Assert.True(new FileInfo(o.Output!).Length <= new FileInfo(src).Length / 2);
        using var image = VImage.NewFromFile(o.Output!);
        Assert.True(VipsCodec.HasTransparency(image));
    }

    [Fact]
    public async Task Edited_pictures_keep_the_original_metadata()
    {
        using var t = new TempFolder();
        string src = Photo(t, "trip.jpg");
        string output = "";
        await StaWorker.RunAsync(() =>
        {
            var bitmap = WicImageCodec.Load(src);
            var cropped = new System.Windows.Media.Imaging.CroppedBitmap(bitmap, new System.Windows.Int32Rect(10, 10, 200, 100));
            output = WicImageCodec.WriteOutput(cropped, src, WicImageCodec.Jpg, numbered: true, Ctx.Default(), CancellationToken.None);
        });
        Assert.Equal((200, 100), Dims(output));
        AssertKeepsMetadata(output);
    }

    [Fact]
    public void A_batch_runs_in_parallel_and_every_file_gets_its_own_result()
    {
        using var t = new TempFolder();
        var sources = Enumerable.Range(1, 8).Select(i => Photo(t, $"p{i}.jpg", 600, 400)).ToList();
        var provider = new ImageConversionProvider();
        var files = new FileSet(sources);
        var op = provider.GetOperations(files, OperationMode.Convert).Single(d => d.Id == "convert.image.webp");
        var c = new Collector();
        provider.ExecuteAsync(op, files, Ctx.Default(), c, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(8, c.Outcomes.Count);
        Assert.All(c.Outcomes, o => Assert.Equal(OutcomeStatus.Succeeded, o.Status));
        Assert.Equal(sources.Select(s => Path.ChangeExtension(s, ".webp")).Order(), c.Outcomes.Select(o => o.Output!).Order());
    }
}
