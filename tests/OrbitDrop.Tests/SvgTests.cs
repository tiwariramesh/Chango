using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using OrbitDrop.Conversion;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Core;

namespace OrbitDrop.Tests;

/// <summary>SVG drawings convert like any image, and an SVG can never reach outside its own file.</summary>
public class SvgTests
{
    private const string Icon = """
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">
          <circle cx="12" cy="12" r="10" fill="#E53935"/>
        </svg>
        """;

    private static async Task<FileOutcome> Convert(string file, string target)
    {
        var provider = new ImageConversionProvider();
        var op = provider.GetOperations(new FileSet([file]), OperationMode.Convert).Single(o => o.Id == $"convert.image.{target}");
        var c = new Collector();
        await provider.ExecuteAsync(op, new FileSet([file]), Ctx.Default(), c, CancellationToken.None);
        return Assert.Single(c.Outcomes);
    }

    private static byte[] PixelAt(string png, int x, int y)
    {
        var px = new byte[4];
        StaWorker.RunAsync(() => WicImageCodec.Load(png).CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0)).GetAwaiter().GetResult();
        return px;
    }

    [Fact]
    public async Task A_small_icon_becomes_a_sharp_png_that_keeps_its_transparency()
    {
        using var t = new TempFolder();
        string src = t.File("icon.svg");
        File.WriteAllText(src, Icon);
        var r = await Convert(src, "png");
        Assert.Equal(OutcomeStatus.Succeeded, r.Status);
        Assert.Equal(t.File("icon.png"), r.Output);
        var (w, h) = (0, 0);
        await StaWorker.RunAsync(() => { var b = WicImageCodec.Load(r.Output!); (w, h) = (b.PixelWidth, b.PixelHeight); });
        Assert.Equal((1024, 1024), (w, h));                // 24 px drawn big enough to be useful
        var centre = PixelAt(r.Output!, 512, 512);          // BGRA
        Assert.True(centre[2] > 200 && centre[1] < 90 && centre[3] == 255, "the circle is red");
        Assert.Equal(0, PixelAt(r.Output!, 5, 5)[3]);       // the corner stays transparent
    }

    [Fact]
    public async Task Svg_goes_to_jpg_on_white()
    {
        using var t = new TempFolder();
        string src = t.File("icon.svg");
        File.WriteAllText(src, Icon);
        var r = await Convert(src, "jpg");
        Assert.Equal(OutcomeStatus.Succeeded, r.Status);
        Assert.All(PixelAt(r.Output!, 5, 5).Take(3), v => Assert.True(v > 235));
    }

    [Fact]
    public async Task An_svg_never_contacts_a_server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var t = new TempFolder();
        string src = t.File("tracker.svg");
        File.WriteAllText(src, $"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="20" height="20">
              <image href="http://127.0.0.1:{port}/pixel.png" width="20" height="20"/>
              <use xlink:href="http://127.0.0.1:{port}/shapes.svg#a"/>
              <rect width="10" height="10" fill="blue"/>
            </svg>
            """);
        var r = await Convert(src, "png");
        Assert.Equal(OutcomeStatus.Succeeded, r.Status);
        await Task.Delay(300);
        Assert.False(listener.Pending(), "the SVG tried to reach the network");
    }

    [Fact]
    public async Task An_svg_never_pulls_in_other_files_from_the_disk()
    {
        using var t = new TempFolder();
        string secret = t.Image("secret.png", 40, 40); // a gradient: never transparent
        string src = t.File("peek.svg");
        File.WriteAllText(src, $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="40" height="40">
              <image href="{new Uri(secret).AbsoluteUri}" width="40" height="40"/>
            </svg>
            """);
        var r = await Convert(src, "png");
        if (r.Status == OutcomeStatus.Succeeded)
            Assert.Equal(0, PixelAt(r.Output!, 512, 512)[3]); // nothing drawn: the file wasn't read
    }

    [Fact]
    public async Task An_entity_bomb_fails_fast_instead_of_freezing()
    {
        using var t = new TempFolder();
        string src = t.File("bomb.svg");
        var dtd = new System.Text.StringBuilder("<!DOCTYPE svg [<!ENTITY a0 \"lol\">");
        for (int i = 1; i <= 9; i++) dtd.Append($"<!ENTITY a{i} \"{string.Concat(Enumerable.Repeat($"&a{i - 1};", 10))}\">");
        dtd.Append("]>");
        File.WriteAllText(src, dtd + """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><text>&a9;</text></svg>""");
        var sw = Stopwatch.StartNew();
        var r = await Convert(src, "png");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
        Assert.Equal(OutcomeStatus.Failed, r.Status);
    }

    [Fact]
    public async Task A_broken_svg_fails_with_a_clear_message()
    {
        using var t = new TempFolder();
        string src = t.File("broken.svg");
        File.WriteAllText(src, "this is not an svg");
        var r = await Convert(src, "png");
        Assert.Equal(OutcomeStatus.Failed, r.Status);
        Assert.Empty(SafeOutput.LeftoverTemps(t.Path));
    }
}
