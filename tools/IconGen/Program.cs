using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// OrbitDrop brand mark v2 (monochrome): a white droplet orbited by a ring, on a glossy near-black squircle tile.
// Realism comes from layered shading: tile depth gradient + top sheen + rim light, a droplet with a soft
// radial body, specular highlight and contact shadow, and a ring that passes behind and in front of the drop.
// At 16–24 px the mark simplifies (flat drop, thicker ring) so it stays crisp in the notification area.

namespace IconGen;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--pfp")
        {
            // Profile pictures (Instagram, TikTok, X …): full-bleed, so the round crop shows the mark, not a tile corner.
            string dir = args.Length > 1 ? args[1] : "marketing/brand";
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "pfp-orbitdrop.png"), Png(RenderAvatar(1080, glow: false)));
            File.WriteAllBytes(Path.Combine(dir, "pfp-orbitdrop-glow.png"), Png(RenderAvatar(1080, glow: true)));
            Console.WriteLine($"Wrote profile pictures to {Path.GetFullPath(dir)}");
            return 0;
        }

        string outDir = args.Length > 0 ? args[0] : "assets/brand";
        Directory.CreateDirectory(outDir);

        int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
        var pngs = icoSizes.Select(s => (Size: s, Data: Png(Render(s)))).ToList();
        File.WriteAllBytes(Path.Combine(outDir, "orbitdrop.ico"), Ico(pngs));
        foreach (int s in new[] { 32, 64, 256, 512, 1024 })
            File.WriteAllBytes(Path.Combine(outDir, $"orbitdrop-{s}.png"), Png(Render(s)));

        Console.WriteLine($"Wrote icon set to {Path.GetFullPath(outDir)}");
        return 0;
    }

    private static Color Rgb(byte r, byte g, byte b, byte a = 255) => Color.FromArgb(a, r, g, b);

    private static BitmapSource Render(int px)
    {
        double s = px;
        bool small = px <= 24;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // ---- tile: squircle with depth
            double inset = small ? 0.5 : s * 0.035;
            var tile = Squircle(new Rect(inset, inset, s - 2 * inset, s - 2 * inset), 5.0);
            dc.DrawGeometry(new LinearGradientBrush(Rgb(0x2E, 0x2E, 0x34), Rgb(0x08, 0x08, 0x0A), 90), null, tile);
            if (!small)
            {
                // soft top sheen
                dc.PushClip(tile);
                dc.DrawEllipse(new RadialGradientBrush(Rgb(255, 255, 255, 34), Rgb(255, 255, 255, 0)) { Center = new Point(0.5, 0.2), GradientOrigin = new Point(0.5, 0.05), RadiusX = 0.7, RadiusY = 0.55 },
                    null, new Point(s * 0.5, s * 0.12), s * 0.62, s * 0.42);
                dc.Pop();
                // rim light along the top edge
                dc.DrawGeometry(null, new Pen(new LinearGradientBrush(Rgb(255, 255, 255, 70), Rgb(255, 255, 255, 0), 90), Math.Max(1, s * 0.006)), tile);
            }

            DrawMark(dc, s, small);
        }

        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// The mark on a full-bleed background of the tile's own colours, for round profile pictures. The mark is drawn a
    /// little smaller so the circle keeps a calm margin; <paramref name="glow"/> adds soft light behind the drop.
    /// </summary>
    private static BitmapSource RenderAvatar(int px, bool glow)
    {
        double s = px;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Rgb(0x2E, 0x2E, 0x34), Rgb(0x08, 0x08, 0x0A), 90), null, new Rect(0, 0, s, s));
            dc.DrawEllipse(new RadialGradientBrush(Rgb(255, 255, 255, 30), Rgb(255, 255, 255, 0)), null, new Point(s * 0.5, s * 0.08), s * 0.7, s * 0.45);
            if (glow)
                dc.DrawEllipse(new RadialGradientBrush(Rgb(255, 255, 255, 46), Rgb(255, 255, 255, 0)), null, new Point(s * 0.5, s * 0.52), s * 0.36, s * 0.36);
            dc.PushTransform(new ScaleTransform(0.86, 0.86, s * 0.5, s * 0.5));
            DrawMark(dc, s, small: false);
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The droplet, its ring (behind and in front) and the satellite, for an <paramref name="s"/>-pixel square.</summary>
    private static void DrawMark(DrawingContext dc, double s, bool small)
    {
        {
            var c = new Point(s * 0.5, s * 0.54);
            double r = s * (small ? 0.2 : 0.165);
            var drop = Droplet(c, r, tipY: s * (small ? 0.2 : 0.235));

            double rx = s * (small ? 0.42 : 0.385), ry = s * (small ? 0.14 : 0.115), tilt = -17;
            var ringCenter = new Point(s * 0.5, c.Y - r * 0.15);
            double ringW = s * (small ? 0.075 : 0.038);
            var rotate = new RotateTransform(tilt, ringCenter.X, ringCenter.Y);

            // ---- ring, back half (behind the drop)
            dc.PushTransform(rotate);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Rgb(255, 255, 255, small ? (byte)150 : (byte)95)), ringW) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                HalfEllipse(ringCenter, rx, ry, top: true));
            dc.Pop();

            // ---- droplet
            if (small)
            {
                dc.DrawGeometry(Brushes.White, null, drop);
            }
            else
            {
                // contact shadow under the drop
                dc.DrawEllipse(new RadialGradientBrush(Rgb(0, 0, 0, 120), Rgb(0, 0, 0, 0)), null, new Point(c.X, c.Y + r * 1.05), r * 1.05, r * 0.28);
                // body: bright top-left, cool grey falloff toward the lower-right edge
                var body = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.36, 0.42), Center = new Point(0.45, 0.5), RadiusX = 0.75, RadiusY = 0.7,
                    GradientStops = { new GradientStop(Rgb(255, 255, 255), 0), new GradientStop(Rgb(0xF1, 0xF1, 0xF4), 0.45), new GradientStop(Rgb(0xB9, 0xBB, 0xC4), 1) },
                };
                dc.DrawGeometry(body, null, drop);
                // inner edge darkening for volume
                dc.DrawGeometry(null, new Pen(new LinearGradientBrush(Rgb(0, 0, 0, 0), Rgb(0x3A, 0x3C, 0x46, 70), 70), r * 0.08), drop);
                // specular highlight
                dc.PushClip(drop);
                dc.DrawEllipse(new RadialGradientBrush(Rgb(255, 255, 255, 235), Rgb(255, 255, 255, 0)), null, new Point(c.X - r * 0.38, c.Y - r * 0.32), r * 0.32, r * 0.42);
                dc.DrawEllipse(new RadialGradientBrush(Rgb(255, 255, 255, 120), Rgb(255, 255, 255, 0)), null, new Point(c.X + r * 0.42, c.Y + r * 0.45), r * 0.18, r * 0.12);
                dc.Pop();
            }

            // ---- ring, front half (in front of the drop) + satellite
            dc.PushTransform(rotate);
            var frontBrush = new LinearGradientBrush(Rgb(255, 255, 255, 200), Rgb(255, 255, 255), 0);
            dc.DrawGeometry(null, new Pen(frontBrush, ringW) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, HalfEllipse(ringCenter, rx, ry, top: false));
            double a = 24 * Math.PI / 180;
            var sat = new Point(ringCenter.X + rx * Math.Cos(a), ringCenter.Y + ry * Math.Sin(a));
            double satR = s * (small ? 0.075 : 0.05);
            if (!small) dc.DrawEllipse(new SolidColorBrush(Rgb(0x0B, 0x0B, 0x0D)), null, sat, satR * 1.28, satR * 1.28); // gap where the moon crosses the ring
            var moon = small ? (Brush)Brushes.White : new RadialGradientBrush(Rgb(255, 255, 255), Rgb(0xC2, 0xC4, 0xCC)) { GradientOrigin = new Point(0.35, 0.35) };
            dc.DrawEllipse(moon, null, sat, satR, satR);
            dc.Pop();
        }
    }

    /// <summary>Teardrop pointing up: round base centred at <paramref name="c"/>, tip at <paramref name="tipY"/>.</summary>
    private static Geometry Droplet(Point c, double r, double tipY)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var tip = new Point(c.X, tipY);
            ctx.BeginFigure(tip, true, true);
            ctx.BezierTo(new Point(c.X - r * 0.12, tipY + (c.Y - tipY) * 0.35), new Point(c.X - r, c.Y - r * 0.85), new Point(c.X - r, c.Y), true, true);
            ctx.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.BezierTo(new Point(c.X + r, c.Y - r * 0.85), new Point(c.X + r * 0.12, tipY + (c.Y - tipY) * 0.35), tip, true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Superellipse ("squircle") filling <paramref name="rect"/>; n≈5 matches modern app-icon shapes.</summary>
    private static Geometry Squircle(Rect rect, double n)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            double a = rect.Width / 2, b = rect.Height / 2, cx = rect.X + a, cy = rect.Y + b;
            const int steps = 180;
            for (int i = 0; i <= steps; i++)
            {
                double t = i * 2 * Math.PI / steps, ct = Math.Cos(t), st = Math.Sin(t);
                var p = new Point(cx + a * Math.Sign(ct) * Math.Pow(Math.Abs(ct), 2 / n), cy + b * Math.Sign(st) * Math.Pow(Math.Abs(st), 2 / n));
                if (i == 0) ctx.BeginFigure(p, true, true); else ctx.LineTo(p, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    private static Geometry HalfEllipse(Point c, double rx, double ry, bool top)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - rx, c.Y), false, false);
            ctx.ArcTo(new Point(c.X + rx, c.Y), new Size(rx, ry), 0, false, top ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    private static byte[] Png(BitmapSource b)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>ICO container with PNG-compressed entries (supported since Windows Vista).</summary>
    private static byte[] Ico(List<(int Size, byte[] Data)> images)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
        return ms.ToArray();
    }
}
