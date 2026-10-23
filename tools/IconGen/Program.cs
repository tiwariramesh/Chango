using SkiaSharp;
using Svg.Skia;

// Chango brand assets from the logo sources in assets/brand/new-logo:
//   chango-background.svg  → the app icon (chango.ico) and the tile images (chango-<size>.png)
//   chango-transparent.svg → the bare mark for glass surfaces (chango-mark-<size>.png)
//   chango-lottie.json     → the launch animation (copied as chango-intro.json)
// Usage: dotnet run --project tools/IconGen [-- <logoFolder> <outputFolder>]

namespace IconGen;

internal static class Program
{
    private static int Main(string[] args)
    {
        string source = args.Length > 0 ? args[0] : Path.Combine("assets", "brand", "new-logo");
        string output = args.Length > 1 ? args[1] : Path.Combine("src", "Chango.UI", "Assets");
        Directory.CreateDirectory(output);

        using var tile = Load(Path.Combine(source, "chango-background.svg"));
        using var mark = Load(Path.Combine(source, "chango-transparent.svg"));

        int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
        File.WriteAllBytes(Path.Combine(output, "chango.ico"), Ico(icoSizes.Select(s => (s, Png(tile, s))).ToList()));
        foreach (int s in new[] { 32, 64, 256, 512, 1024 })
            File.WriteAllBytes(Path.Combine(output, $"chango-{s}.png"), Png(tile, s));
        foreach (int s in new[] { 64, 256, 512 })
            File.WriteAllBytes(Path.Combine(output, $"chango-mark-{s}.png"), Png(mark, s));
        File.Copy(Path.Combine(source, "chango-lottie.json"), Path.Combine(output, "chango-intro.json"), overwrite: true);

        Console.WriteLine($"Wrote the Chango icon set to {Path.GetFullPath(output)}");
        return 0;
    }

    private static SKSvg Load(string path)
    {
        var svg = new SKSvg();
        if (svg.Load(path) is null) throw new InvalidDataException($"Couldn't read {path}");
        return svg;
    }

    /// <summary>The whole SVG canvas (its viewBox) scaled into a <paramref name="size"/>-pixel square, as PNG.</summary>
    private static byte[] Png(SKSvg svg, int size)
    {
        var picture = svg.Picture!;
        var bounds = picture.CullRect;
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            float scale = size / Math.Max(bounds.Width, bounds.Height);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>An ICO file whose entries are PNG images (supported since Windows Vista).</summary>
    private static byte[] Ico(List<(int Size, byte[] Png)> images)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); // reserved
        w.Write((ushort)1); // type: icon
        w.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            byte side = (byte)(size >= 256 ? 0 : size); // 0 means 256
            w.Write(side); w.Write(side);
            w.Write((byte)0); w.Write((byte)0); // palette, reserved
            w.Write((ushort)1); w.Write((ushort)32); // planes, bits per pixel
            w.Write(png.Length); w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
        return ms.ToArray();
    }
}
