using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chango.Conversion;
using Chango.Core;
using Chango.Tools.Metadata;
using Windows.Storage;

namespace Chango.Tests;

public class MetadataStripperTests
{
    private static string Jpeg(TempFolder t, string name, ushort orientation, string title)
    {
        string path = t.File(name);
        StaWorker.RunAsync(() =>
        {
            var bmp = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgr24, null, new byte[40 * 20 * 3], 40 * 3);
            var meta = new BitmapMetadata("jpg");
            meta.SetQuery("/app1/ifd/{ushort=274}", orientation);
            meta.Title = title;
            meta.CameraModel = "SecretCam 9000";
            var enc = new JpegBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp, null, meta, null));
            using var fs = File.Create(path);
            enc.Save(fs);
        }).GetAwaiter().GetResult();
        return path;
    }

    private static bool Contains(byte[] data, string text) =>
        data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0 || data.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0;

    [Fact]
    public void Jpeg_loses_its_metadata_but_keeps_its_orientation_and_pixels()
    {
        using var t = new TempFolder();
        string src = Jpeg(t, "phone.jpg", 6, "Holiday");
        byte[] before = File.ReadAllBytes(src);
        Assert.True(Contains(before, "SecretCam"));

        byte[] after = MetadataStripper.Jpeg(before);
        Assert.False(Contains(after, "SecretCam"));
        Assert.False(Contains(after, "Holiday"));
        Assert.Equal(6, MetadataStripper.JpegOrientation(after));
        // The compressed image data is untouched: the tail of the file is identical.
        Assert.True(before.AsSpan(before.Length - 200).SequenceEqual(after.AsSpan(after.Length - 200)));

        string copy = t.File("stripped.jpg");
        File.WriteAllBytes(copy, after);
        int w = 0, h = 0;
        StaWorker.RunAsync(() => { var b = Chango.Conversion.Imaging.WicImageCodec.Load(copy); w = b.PixelWidth; h = b.PixelHeight; }).GetAwaiter().GetResult();
        Assert.Equal((20, 40), (w, h)); // still displayed upright (rotated by its orientation)
    }

    [Fact]
    public void Upright_jpeg_gets_no_exif_at_all()
    {
        using var t = new TempFolder();
        byte[] after = MetadataStripper.Jpeg(File.ReadAllBytes(Jpeg(t, "a.jpg", 1, "x")));
        Assert.False(Contains(after, "Exif"));
    }

    [Fact]
    public void Png_text_chunks_are_removed()
    {
        using var t = new TempFolder();
        byte[] png = File.ReadAllBytes(t.Image("a.png", 8, 8));
        // Insert a tEXt chunk after IHDR (8-byte signature + 25-byte IHDR chunk).
        byte[] text = [0, 0, 0, 12, (byte)'t', (byte)'E', (byte)'X', (byte)'t', .. Encoding.ASCII.GetBytes("Author\0Alice"), 0, 0, 0, 0];
        byte[] tagged = [.. png[..33], .. text, .. png[33..]];
        byte[] after = MetadataStripper.Png(tagged);
        Assert.False(Contains(after, "Alice"));
        Assert.Equal(png, after);
    }

    [Fact]
    public void Mp3_id3_tags_at_both_ends_are_removed()
    {
        byte[] audio = [0xFF, 0xFB, 0x90, 0x64, 1, 2, 3, 4, 5, 6];
        byte[] id3v2 = [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 5, 9, 9, 9, 9, 9];
        byte[] id3v1 = [(byte)'T', (byte)'A', (byte)'G', .. new byte[125]];
        Assert.Equal(audio, MetadataStripper.Mp3([.. id3v2, .. audio, .. id3v1]));
    }

    [Fact]
    public void Flac_keeps_stream_info_and_audio_but_not_tags()
    {
        byte[] streamInfo = [0x00, 0, 0, 34, .. new byte[34]];
        byte[] comment = [0x84, 0, 0, 5, .. Encoding.ASCII.GetBytes("Alice")]; // last block, type 4
        byte[] frames = [0xFF, 0xF8, 1, 2, 3];
        byte[] after = MetadataStripper.Flac([.. "fLaC"u8.ToArray(), .. streamInfo, .. comment, .. frames]);
        Assert.Equal([.. "fLaC"u8.ToArray(), 0x80, 0, 0, 34, .. new byte[34], .. frames], after);
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 1u)]
    [InlineData(3, 50u)]
    [InlineData(5, 99u)]
    public void Ratings_round_trip(int stars, uint value)
    {
        Assert.Equal(value, FileProperties.RatingToProperty(stars));
        Assert.Equal(stars, FileProperties.RatingFromProperty(value));
    }
}

public class MetadataEditTests
{
    private static async Task<FileOutcome> Run(string id, string file, object? input = null)
    {
        var provider = new MetadataProvider();
        var files = new FileSet([file]);
        var op = provider.GetOperations(files, OperationMode.Tools).Single(d => d.Id == id);
        var c = new Collector();
        await provider.ExecuteAsync(op, files, Ctx.Default() with { Input = input }, c, CancellationToken.None);
        return Assert.Single(c.Outcomes);
    }

    [Fact]
    public async Task Editing_details_writes_a_copy_and_leaves_the_original_alone()
    {
        using var t = new TempFolder();
        string src = t.Image("photo.jpg", 64, 48);
        string hash = SafeOutput.Sha256(src);
        var edit = new MetadataEdit(new Dictionary<string, string?> { ["System.Title"] = "Sunset", ["System.Rating"] = "4", ["System.Keywords"] = "beach; summer" }, false);

        var o = await Run(MetadataProvider.InfoId, src, edit);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        Assert.Equal(t.File("photo (edited).jpg"), o.Output);
        Assert.Equal(hash, SafeOutput.Sha256(src));
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));

        var snapshot = await FileProperties.ReadAsync(o.Output!);
        Assert.Equal("Sunset", snapshot.Fields.Single(f => f.Key == "System.Title").Value);
        Assert.Equal("4", snapshot.Fields.Single(f => f.Key == "System.Rating").Value);
        Assert.Equal("beach; summer", snapshot.Fields.Single(f => f.Key == "System.Keywords").Value);
    }

    [Fact]
    public async Task Remove_location_takes_out_gps_only()
    {
        using var t = new TempFolder();
        string src = t.File("trip.jpg");
        StaWorker.RunAsync(() =>
        {
            // EXIF GPS rationals are packed as numerator (low 32 bits) / denominator (high 32 bits).
            static ulong R(uint n) => n | (1UL << 32);
            var bmp = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgr24, null, new byte[64 * 48 * 3], 64 * 3);
            var meta = new BitmapMetadata("jpg") { Title = "Keep me" };
            meta.SetQuery("/app1/ifd/gps/{ushort=1}", "N");
            meta.SetQuery("/app1/ifd/gps/{ushort=2}", new[] { R(51), R(30), R(0) });
            meta.SetQuery("/app1/ifd/gps/{ushort=3}", "W");
            meta.SetQuery("/app1/ifd/gps/{ushort=4}", new[] { R(0), R(7), R(0) });
            var enc = new JpegBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp, null, meta, null));
            using var fs = File.Create(src);
            enc.Save(fs);
        }).GetAwaiter().GetResult();
        Assert.True((await FileProperties.ReadAsync(src)).HasLocation);

        var o = await Run("tool.meta.location", src);
        Assert.True(o.Status == OutcomeStatus.Succeeded, o.Message);
        var after = await FileProperties.ReadAsync(o.Output!);
        Assert.False(after.HasLocation);
        Assert.Equal("Keep me", after.Fields.Single(f => f.Key == "System.Title").Value);
    }

    [Fact]
    public void Info_is_for_single_files_and_remove_all_only_where_lossless()
    {
        var provider = new MetadataProvider();
        string a = Path.GetTempFileName();
        try
        {
            string png = Path.ChangeExtension(a, ".png");
            File.Move(a, png);
            var ids = provider.GetOperations(new FileSet([png]), OperationMode.Tools).Select(o => o.Id).ToList();
            Assert.Equal([MetadataProvider.InfoId, "tool.meta.strip"], ids);
            File.Delete(png);
        }
        finally { File.Delete(a); }
    }
}
