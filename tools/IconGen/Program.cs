using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Flipr brand mark: a white file card flipping over another, with a curved flip arrow, on a Flip Teal (#0EA5A4)
// squircle tile. Depth comes from the tile gradient + top sheen + rim light and a soft shadow under the front card.
// At 16–24 px the mark simplifies (two flat cards, no arrow or lines) so it stays crisp in the notification area.

namespace IconGen;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--pfp")
        {
            // Profile pictures (Instagram, TikTok, X …): full-bleed, so the round crop shows the mark, not a tile corner.
            string dir = args.Length > 1 ? args[1] : "assets/brand";
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "pfp-flipr.png"), Png(RenderAvatar(1080, glow: false)));
            File.WriteAllBytes(Path.Combine(dir, "pfp-flipr-glow.png"), Png(RenderAvatar(1080, glow: true)));
            Console.WriteLine($"Wrote profile pictures to {Path.GetFullPath(dir)}");
            return 0;
        }

        string outDir = args.Length > 0 ? args[0] : "assets/brand";
        Directory.CreateDirectory(outDir);

        int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
        var pngs = icoSizes.Select(s => (Size: s, Data: Png(Render(s)))).ToList();
        File.WriteAllBytes(Path.Combine(outDir, "flipr.ico"), Ico(pngs));
        foreach (int s in new[] { 32, 64, 256, 512, 1024 })
            File.WriteAllBytes(Path.Combine(outDir, $"flipr-{s}.png"), Png(Render(s)));

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
            dc.DrawGeometry(new LinearGradientBrush(Rgb(0x1B, 0xC7, 0xC3), Rgb(0x0A, 0x85, 0x84), 90), null, tile);
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
            dc.DrawRectangle(new LinearGradientBrush(Rgb(0x1B, 0xC7, 0xC3), Rgb(0x0A, 0x85, 0x84), 90), null, new Rect(0, 0, s, s));
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

    /// <summary>
    /// The Flipr mark for an <paramref name="s"/>-pixel square: a file card flipping over another (the one behind is
    /// tilted, mid-turn) with a curved arrow showing the flip. The front card's folded corner is Flip Teal.
    /// </summary>
    private static void DrawMark(DrawingContext dc, double s, bool small)
    {
        double radius = s * (small ? 0.06 : 0.05);

        // ---- back card, tilted: the file before it flips
        var back = new RectangleGeometry(new Rect(s * 0.2, s * 0.22, s * 0.36, s * 0.46), radius, radius,
            new RotateTransform(-14, s * 0.38, s * 0.45));
        dc.DrawGeometry(new SolidColorBrush(Rgb(255, 255, 255, small ? (byte)140 : (byte)105)), null, back);

        // ---- front card with a folded top-right corner: the converted file
        var front = new Rect(s * 0.4, s * 0.3, s * 0.38, s * 0.5);
        double fold = s * (small ? 0.15 : 0.13);
        var card = Card(front, radius, fold);
        if (!small)
            dc.DrawGeometry(new SolidColorBrush(Rgb(0x02, 0x3A, 0x39, 70)), null, Card(Rect.Offset(front, s * 0.012, s * 0.022), radius, fold));
        Brush paper = small ? Brushes.White : new LinearGradientBrush(Rgb(255, 255, 255), Rgb(0xE4, 0xF6, 0xF5), 90);
        dc.DrawGeometry(paper, null, card);

        var flap = new StreamGeometry();
        using (var ctx = flap.Open())
        {
            ctx.BeginFigure(new Point(front.Right - fold, front.Top), true, true);
            ctx.LineTo(new Point(front.Right - fold, front.Top + fold), true, true);
            ctx.LineTo(new Point(front.Right, front.Top + fold), true, true);
        }
        flap.Freeze();
        dc.DrawGeometry(new SolidColorBrush(small ? Rgb(0x0A, 0x7C, 0x7B) : Rgb(0x0E, 0xA5, 0xA4)), null, flap);

        if (small) return; // at tray sizes the two cards alone read best

        // ---- text lines on the front card
        var line = new SolidColorBrush(Rgb(0x0E, 0xA5, 0xA4, 150));
        foreach (double y in new[] { 0.52, 0.6, 0.68 })
        {
            double right = y < 0.55 ? 0.68 : 0.72;
            dc.DrawRoundedRectangle(line, null, new Rect(s * 0.47, s * y, s * (right - 0.47), s * 0.032), s * 0.016, s * 0.016);
        }

        // ---- the flip: an arc over the top, from the back card to the front card, ending in an arrowhead
        var arc = new StreamGeometry();
        var from = new Point(s * 0.27, s * 0.17);
        var to = new Point(s * 0.6, s * 0.17);
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(from, false, false);
            ctx.ArcTo(to, new Size(s * 0.19, s * 0.12), 0, false, SweepDirection.Clockwise, true, true);
        }
        arc.Freeze();
        var pen = new Pen(Brushes.White, s * 0.04) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform(0, s * 0.012));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Rgb(0x02, 0x3A, 0x39, 60)), pen.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        dc.Pop();
        dc.DrawGeometry(null, pen, arc);
        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(new Point(to.X - s * 0.075, to.Y - s * 0.035), false, false);
            ctx.LineTo(to, true, true);
            ctx.LineTo(new Point(to.X - s * 0.02, to.Y - s * 0.085), true, true);
        }
        head.Freeze();
        dc.DrawGeometry(null, pen, head);
    }

    /// <summary>A rounded card whose top-right corner is cut diagonally for a folded corner of size <paramref name="fold"/>.</summary>
    private static Geometry Card(Rect r, double radius, double fold)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(r.Left + radius, r.Top), true, true);
            ctx.LineTo(new Point(r.Right - fold, r.Top), true, true);
            ctx.LineTo(new Point(r.Right, r.Top + fold), true, true);
            ctx.LineTo(new Point(r.Right, r.Bottom - radius), true, true);
            ctx.ArcTo(new Point(r.Right - radius, r.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(r.Left + radius, r.Bottom), true, true);
            ctx.ArcTo(new Point(r.Left, r.Bottom - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(r.Left, r.Top + radius), true, true);
            ctx.ArcTo(new Point(r.Left + radius, r.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
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
