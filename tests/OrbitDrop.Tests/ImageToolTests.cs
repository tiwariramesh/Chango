using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Conversion;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Core;
using OrbitDrop.Tools;

namespace OrbitDrop.Tests;

public class OperationMenuTests
{
    private static OperationDescriptor Op(string id, int order, OperationGroup? g = null) =>
        new(id, "p", OperationMode.Tools, id, null, "%1", id, id, order, Group: g);

    [Fact]
    public void Groups_collapse_into_one_header_at_their_first_position_and_drop_runs_the_default()
    {
        var g = new OperationGroup("g", "Compress", null);
        var flat = new[] { Op("a", 1, g), Op("b", 2), Op("c", 3, g) };
        var top = OperationMenu.TopLevel(flat);
        Assert.Equal(["group.g", "b"], top.Select(o => o.Id));
        Assert.True(top[0].IsGroupHeader);
        Assert.Equal("Compress", top[0].Label);
        Assert.Equal("a", OperationMenu.DefaultFor(flat, top[0]).Id);
        Assert.Equal(["a", "c"], OperationMenu.Members(flat, "g").Select(o => o.Id));
    }

    [Fact]
    public void A_group_with_one_option_shows_the_option_itself()
    {
        var flat = new[] { Op("only", 1, new OperationGroup("g", "G", null)) };
        Assert.Equal("only", Assert.Single(OperationMenu.TopLevel(flat)).Id);
    }
}

public class ImageToolTests
{
    private static readonly ImageToolsProvider Provider = new();

    private static FileOutcome Run(string id, string file)
    {
        var files = new FileSet([file]);
        var op = Provider.GetOperations(files, OperationMode.Tools).Single(d => d.Id == id);
        var c = new Collector();
        Provider.ExecuteAsync(op, files, Ctx.Default(), c, CancellationToken.None).GetAwaiter().GetResult();
        return Assert.Single(c.Outcomes);
    }

    private static (int W, int H) Size(string file)
    {
        (int, int) size = default;
        StaWorker.RunAsync(() => { var b = WicImageCodec.Load(file); size = (b.PixelWidth, b.PixelHeight); }).GetAwaiter().GetResult();
        return size;
    }

    /// <summary>A photo-like (noisy) opaque image, so JPEG sizes behave like real photos.</summary>
    private static string Noisy(TempFolder t, string name, int w, int h)
    {
        string path = t.File(name);
        StaWorker.RunAsync(() =>
        {
            var rng = new Random(7);
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                int x = i / 4 % w;
                px[i] = (byte)(x * 255 / w); px[i + 1] = (byte)rng.Next(256); px[i + 2] = (byte)rng.Next(120, 200); px[i + 3] = 255;
            }
            var enc = new JpegBitmapEncoder { QualityLevel = 95 };
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
            using var fs = File.Create(path);
            enc.Save(fs);
        }).GetAwaiter().GetResult();
        return path;
    }

    [Fact]
    public void Image_tools_show_edit_then_grouped_tools()
    {
        var top = OperationMenu.TopLevel(Provider.GetOperations(new FileSet([@"C:\x\a.png"]), OperationMode.Tools).ToList());
        Assert.Equal(["Edit", "Compress", "Resize", "Rotate & flip", "Grayscale"], top.Select(o => o.Label));
    }

    [Fact]
    public void Compress_by_half_roughly_halves_a_photo()
    {
        using var t = new TempFolder();
        string src = Noisy(t, "photo.jpg", 1200, 900);
        long before = new FileInfo(src).Length;
        var o = Run("tool.image.compress.half", src);
        Assert.Equal(OutcomeStatus.Succeeded, o.Status);
        Assert.Equal(t.File("photo (compressed).jpg"), o.Output);
        Assert.InRange(new FileInfo(o.Output!).Length, 1, before / 2);
    }

    [Fact]
    public void Compress_under_a_ceiling_hits_it_and_skips_files_already_small()
    {
        using var t = new TempFolder();
        string big = Noisy(t, "big.jpg", 1600, 1200);
        var o = Run("tool.image.compress.200kb", big);
        Assert.Equal(OutcomeStatus.Succeeded, o.Status);
        Assert.True(new FileInfo(o.Output!).Length <= 200_000);

        string small = t.Image("small.png", 64, 64);
        Assert.Equal(OutcomeStatus.Skipped, Run("tool.image.compress.200kb", small).Status);
    }

    [Fact]
    public void Transparent_png_stays_png_when_compressed()
    {
        using var t = new TempFolder();
        string src = t.Image("logo.png", 900, 900, hole: true);
        var o = Run("tool.image.compress.p25", src);
        if (o.Status == OutcomeStatus.Succeeded) Assert.EndsWith(".png", o.Output);
    }

    [Fact]
    public void Resize_fits_the_long_side_and_never_upscales()
    {
        using var t = new TempFolder();
        string src = t.Image("wide.png", 1600, 1000);
        var o = Run("tool.image.resize.800", src);
        Assert.Equal((800, 500), Size(o.Output!));
        Assert.Equal(OutcomeStatus.Skipped, Run("tool.image.resize.1920", src).Status);
        Assert.Equal((800, 500), Size(Run("tool.image.resize.half", src).Output!));
    }

    [Fact]
    public void Exif_orientation_is_applied_on_load()
    {
        using var t = new TempFolder();
        string path = t.File("phone.jpg");
        StaWorker.RunAsync(() =>
        {
            var bmp = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgr24, null, new byte[40 * 20 * 3], 40 * 3);
            var meta = new BitmapMetadata("jpg");
            meta.SetQuery("/app1/ifd/{ushort=274}", (ushort)6); // stored landscape, displayed rotated 90°
            var enc = new JpegBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp, null, meta, null));
            using var fs = File.Create(path);
            enc.Save(fs);
        }).GetAwaiter().GetResult();
        Assert.Equal((20, 40), Size(path));
    }
}

public class WicCodecTests
{
    [Fact]
    public void Wic_reports_the_built_in_decoders()
    {
        var exts = OrbitDrop.Conversion.Imaging.WicCodecs.DecoderExtensions;
        Assert.Null(OrbitDrop.Conversion.Imaging.WicCodecs.LastError);
        Assert.Contains(".png", exts);
        Assert.Contains(".jpg", exts);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "orbitdrop-wic.txt"), string.Join(" ", exts.Order()));
    }
}

public class WheelFilterTests
{
    private static readonly OperationGroup Compress = new("image.compress", "Compress", null);
    private static OperationDescriptor Op(string id, OperationGroup? g = null) => new(id, "p", OperationMode.Tools, id, null, "%1", id, id, 0, Group: g);

    [Fact]
    public void Hides_single_options_and_whole_groups()
    {
        var ops = new[] { Op("a"), Op("half", Compress), Op("p25", Compress), Op("b") };
        Assert.Equal(["a", "half", "b"], WheelFilter.Apply(ops, new HashSet<string> { "p25" }).Select(o => o.Id));
        Assert.Equal(["a", "b"], WheelFilter.Apply(ops, new HashSet<string> { WheelFilter.GroupKey(Compress) }).Select(o => o.Id));
    }

    [Fact]
    public void Options_carrying_a_folder_can_only_be_hidden_with_their_group()
    {
        var copyTo = new OperationGroup("actions.copy-to", "Copy to", null);
        var desktop = Op(@"action.copy-to|C:\Users\You\Desktop", copyTo);
        Assert.Null(WheelFilter.OptionKey(desktop));
        Assert.True(WheelFilter.IsHidden(desktop, new HashSet<string> { WheelFilter.GroupKey(copyTo) }));
    }

    [Fact]
    public void A_drop_on_a_group_uses_its_first_visible_choice()
    {
        var flat = WheelFilter.Apply([Op("half", Compress), Op("p75", Compress), Op("p25", Compress)], new HashSet<string> { "half" });
        var header = OperationMenu.TopLevel(flat)[0];
        Assert.Equal("p75", OperationMenu.DefaultFor(flat, header).Id);
    }
}

public class WheelOrderTests
{
    private static OperationDescriptor Op(string id, int order, OperationGroup? g = null) => new(id, "p", OperationMode.Convert, id, null, "%1", id, id, order, Group: g);

    [Fact]
    public void Arranged_slices_come_first_and_groups_move_together()
    {
        var g = new OperationGroup("compress", "Compress", null);
        var ops = new[] { Op("png", 1), Op("jpg", 2), Op("half", 3, g), Op("p75", 4, g), Op("pdf", 5) };
        var ordered = WheelOrder.Apply(ops, ["pdf", WheelFilter.GroupKey(g)]);
        Assert.Equal(["pdf", "half", "p75", "png", "jpg"], ordered.Select(o => o.Id));
        Assert.Equal(["pdf", "group.compress", "png", "jpg"], OperationMenu.TopLevel(ordered).Select(o => o.Id));
    }

    [Fact]
    public void No_arrangement_keeps_the_default_order()
    {
        var ops = new[] { Op("a", 1), Op("b", 2) };
        Assert.Same(ops, WheelOrder.Apply(ops, []));
    }
}
