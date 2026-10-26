using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;
using Svg.Skia;

// Chango brand assets from the logo source in assets/brand/new-logo:
//   chango-mark.svg → the app icon (chango.ico), the PNG set (chango-<size>.png, one per size so each surface loads
//                      the smallest image that's still sharp) and the launch animation (chango-intro.json), in which
//                      each segment of the mark pops in
// Usage: dotnet run --project tools/IconGen [-- <logoFolder> <outputFolder>]

namespace IconGen;

internal static class Program
{
    private static int Main(string[] args)
    {
        string source = args.Length > 0 ? args[0] : Path.Combine("assets", "brand", "new-logo");
        string output = args.Length > 1 ? args[1] : Path.Combine("src", "Chango.UI", "Assets");
        Directory.CreateDirectory(output);

        string markPath = Path.Combine(source, "chango-mark.svg");
        using var mark = Load(markPath);

        int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
        File.WriteAllBytes(Path.Combine(output, "chango.ico"), Ico(icoSizes.Select(s => (s, Png(mark, s))).ToList()));
        foreach (int s in new[] { 16, 24, 32, 48, 64, 128, 256, 512, 1024 })
            File.WriteAllBytes(Path.Combine(output, $"chango-{s}.png"), Png(mark, s));
        File.WriteAllText(Path.Combine(output, "chango-intro.json"), Intro(File.ReadAllText(markPath)));

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

    // ------------------------------------------------------------------ launch animation

    private const int Canvas = 1024, Fps = 120, Frames = 120, Stagger = 10;

    /// <summary>The mark's share of the animation's canvas: the same breathing room the splash and onboarding were laid out with.</summary>
    private const double Fill = 0.76;

    /// <summary>
    /// A Lottie file (Skottie plays it in LottieView) in which each segment of the mark pops in around the "C", from
    /// the top right anticlockwise: fade in and grow from 50% to 110% and back to 100%, about one second in all.
    /// </summary>
    private static string Intro(string svg)
    {
        // chango-mark.svg is a single filled path on a square viewBox.
        string Attr(string name) => Regex.Match(svg, $"\\s{name}=\"([^\"]+)\"") is { Success: true } a
            ? a.Groups[1].Value : throw new InvalidDataException($"chango-mark.svg has no {name}");
        string viewBox = Attr("viewBox"), d = Attr("d"), hex = Attr("fill").TrimStart('#');

        float[] vb = viewBox.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        double scale = Canvas * Fill / Math.Max(vb[2], vb[3]);
        double cx = vb[0] + vb[2] / 2, cy = vb[1] + vb[3] / 2;
        SKPoint Map(SKPoint p) => new((float)((p.X - cx) * scale + Canvas / 2.0), (float)((p.Y - cy) * scale + Canvas / 2.0));

        using var path = SKPath.ParseSvgPathData(d) ?? throw new InvalidDataException("Couldn't read the mark's path");
        var pieces = Segments(path, Map);
        var centre = new SKPoint(Canvas / 2f, Canvas / 2f);
        // Order around the C: the top-right segment first, then anticlockwise (screen y points down).
        pieces = pieces.OrderBy(p => Angle(p.Centre - centre)).ToList();

        double[] fill = [Convert.ToInt32(hex[..2], 16) / 255.0, Convert.ToInt32(hex[2..4], 16) / 255.0, Convert.ToInt32(hex[4..], 16) / 255.0];
        var layers = pieces.Select((p, i) => Layer(i + 1, p, fill, i * Stagger)).ToArray();
        var lottie = new Dictionary<string, object>
        {
            ["v"] = "5.9.4", ["fr"] = Fps, ["ip"] = 0, ["op"] = Frames, ["w"] = Canvas, ["h"] = Canvas, ["ddd"] = 0,
            ["markers"] = Array.Empty<object>(), ["assets"] = Array.Empty<object>(), ["layers"] = layers,
        };
        return JsonSerializer.Serialize(lottie);
    }

    /// <summary>Angle anticlockwise from just below the top-right segment (30° above east), in [0, 360).</summary>
    private static double Angle(SKPoint v)
    {
        double a = Math.Atan2(-v.Y, v.X) * 180 / Math.PI; // anticlockwise from east
        return ((a - 30) % 360 + 360) % 360;
    }

    private sealed record Piece(List<SKPoint> V, List<SKPoint> In, List<SKPoint> Out, SKPoint Centre);

    /// <summary>Each closed subpath of the mark as Lottie vertices with in and out tangents (relative to their vertex).</summary>
    private static List<Piece> Segments(SKPath path, Func<SKPoint, SKPoint> map)
    {
        var pieces = new List<Piece>();
        List<SKPoint> v = [], ins = [], outs = [];
        void Finish()
        {
            if (v.Count > 1 && SKPoint.Distance(v[0], v[^1]) < 0.01f) { ins[0] = ins[^1]; v.RemoveAt(v.Count - 1); ins.RemoveAt(ins.Count - 1); outs.RemoveAt(outs.Count - 1); }
            if (v.Count >= 3)
            {
                float l = v.Min(p => p.X), r = v.Max(p => p.X), t = v.Min(p => p.Y), b = v.Max(p => p.Y);
                pieces.Add(new Piece(v, ins, outs, new SKPoint((l + r) / 2, (t + b) / 2)));
            }
            v = []; ins = []; outs = [];
        }

        using var it = path.CreateRawIterator();
        var pts = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = it.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    Finish();
                    v.Add(map(pts[0])); ins.Add(SKPoint.Empty); outs.Add(SKPoint.Empty);
                    break;
                case SKPathVerb.Line:
                    v.Add(map(pts[1])); ins.Add(SKPoint.Empty); outs.Add(SKPoint.Empty);
                    break;
                case SKPathVerb.Cubic:
                    var end = map(pts[3]);
                    outs[^1] = map(pts[1]) - v[^1];
                    v.Add(end); ins.Add(map(pts[2]) - end); outs.Add(SKPoint.Empty);
                    break;
                case SKPathVerb.Close:
                    Finish();
                    break;
                default:
                    throw new InvalidDataException($"chango-mark.svg uses a {verb} segment; only lines and cubic curves are supported");
            }
        }
        Finish();
        return pieces;
    }

    private static object Layer(int index, Piece p, double[] colour, int start)
    {
        static double R(double x) => Math.Round(x, 2);
        static double[][] Pts(List<SKPoint> list) => list.Select(q => new[] { R(q.X), R(q.Y) }).ToArray();
        static object Still(object value) => new Dictionary<string, object> { ["a"] = 0, ["k"] = value };
        double[] centre = [R(p.Centre.X), R(p.Centre.Y)];

        var transform = new Dictionary<string, object>
        {
            ["ty"] = "tr", ["a"] = Still(centre), ["p"] = Still(centre), ["r"] = Still(0), ["sk"] = Still(0), ["sa"] = Still(0),
            ["s"] = new Dictionary<string, object>
            {
                ["a"] = 1,
                ["k"] = new object[]
                {
                    new Dictionary<string, object> { ["t"] = start, ["s"] = new[] { 50, 50 }, ["o"] = new { x = 0.25, y = 0.67 }, ["i"] = new { x = 0.75, y = 1 } },
                    new Dictionary<string, object> { ["t"] = start + 33, ["s"] = new[] { 110, 110 }, ["o"] = new { x = 0.25, y = 0 }, ["i"] = new { x = 0.75, y = 0.33 } },
                    new Dictionary<string, object> { ["t"] = start + 50, ["s"] = new[] { 100, 100 } },
                },
            },
            ["o"] = new Dictionary<string, object>
            {
                ["a"] = 1,
                ["k"] = new object[]
                {
                    new Dictionary<string, object> { ["t"] = start, ["s"] = new[] { 0 }, ["o"] = new { x = 0, y = 0 }, ["i"] = new { x = 1, y = 1 } },
                    new Dictionary<string, object> { ["t"] = start + 50, ["s"] = new[] { 100 } },
                },
            },
        };
        var group = new Dictionary<string, object>
        {
            ["ty"] = "gr",
            ["it"] = new object[]
            {
                new Dictionary<string, object> { ["ty"] = "sh", ["ks"] = Still(new { c = true, v = Pts(p.V), i = Pts(p.In), o = Pts(p.Out) }) },
                new Dictionary<string, object> { ["ty"] = "fl", ["r"] = 1, ["o"] = Still(100), ["c"] = Still(colour.Select(c => Math.Round(c, 6)).ToArray()) },
                transform,
            },
        };
        return new Dictionary<string, object>
        {
            ["ty"] = 4, ["ind"] = index, ["ddd"] = 0, ["sr"] = 1, ["st"] = 0, ["ip"] = 0, ["op"] = Frames, ["ao"] = 0, ["bm"] = 0,
            ["ks"] = new Dictionary<string, object>
            {
                ["a"] = Still(new[] { 0, 0 }), ["p"] = Still(new[] { 0, 0 }), ["s"] = Still(new[] { 100, 100 }),
                ["r"] = Still(0), ["o"] = Still(100), ["sk"] = Still(0), ["sa"] = Still(0),
            },
            ["shapes"] = new[] { group },
        };
    }
}
