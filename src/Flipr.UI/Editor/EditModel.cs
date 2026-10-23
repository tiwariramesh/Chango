using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Flipr.UI.Editor;

/// <summary>Something drawn on the photo, in image pixel coordinates.</summary>
public abstract record Mark(Color Color, double Width);
public sealed record PenMark(Color Color, double Width, IReadOnlyList<Point> Points) : Mark(Color, Width);
public sealed record ArrowMark(Color Color, double Width, Point From, Point To) : Mark(Color, Width);
public sealed record BoxMark(Color Color, double Width, Rect Area) : Mark(Color, Width);
public sealed record TextMark(Color Color, double Width, Point At, string Text, double Size) : Mark(Color, Width);
public enum RedactKind { Black, Pixelate }
public sealed record RedactMark(Rect Area, RedactKind Kind) : Mark(Colors.Black, 0);

/// <summary>Brightness, contrast, saturation and warmth, each −1…+1 (0 = unchanged).</summary>
public sealed record Adjustments(double Brightness = 0, double Contrast = 0, double Saturation = 0, double Warmth = 0)
{
    public bool IsIdentity => Brightness == 0 && Contrast == 0 && Saturation == 0 && Warmth == 0;
}

/// <summary>
/// Pixel and drawing operations shared by the live preview and the full-resolution export, so what you see is what
/// gets saved. Bitmaps are BGRA32.
/// </summary>
public static class EditRenderer
{
    public static byte[] Pixels(BitmapSource bgra)
    {
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        return px;
    }

    public static BitmapSource FromPixels(byte[] px, int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    /// <summary>Applies <paramref name="a"/> in place. A lookup table keeps it fast enough for live sliders.</summary>
    public static void Adjust(byte[] px, Adjustments a)
    {
        if (a.IsIdentity) return;
        // Brightness shifts, contrast pivots around mid-grey; warmth nudges red up and blue down (or the reverse).
        double contrast = a.Contrast >= 0 ? 1 + a.Contrast * 1.5 : 1 + a.Contrast * 0.8;
        var lut = new byte[3][];
        for (int c = 0; c < 3; c++)
        {
            lut[c] = new byte[256];
            double warm = c == 2 ? a.Warmth * 28 : c == 0 ? -a.Warmth * 28 : a.Warmth * 6; // BGRA: 0 = blue, 2 = red
            for (int v = 0; v < 256; v++)
            {
                double x = (v - 128) * contrast + 128 + a.Brightness * 80 + warm;
                lut[c][v] = (byte)Math.Clamp(Math.Round(x), 0, 255);
            }
        }
        double sat = 1 + a.Saturation;
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = lut[0][px[i]], g = lut[1][px[i + 1]], r = lut[2][px[i + 2]];
            if (sat != 1)
            {
                double y = 0.0722 * b + 0.7152 * g + 0.2126 * r;
                b = y + (b - y) * sat; g = y + (g - y) * sat; r = y + (r - y) * sat;
            }
            px[i] = (byte)Math.Clamp(b, 0, 255);
            px[i + 1] = (byte)Math.Clamp(g, 0, 255);
            px[i + 2] = (byte)Math.Clamp(r, 0, 255);
        }
    }

    /// <summary>Pixelates <paramref name="area"/> (image coordinates scaled by <paramref name="scale"/>) in place.</summary>
    public static void Pixelate(byte[] px, int w, int h, Rect area, double scale, double imageLongSide)
    {
        int block = Math.Max(4, (int)Math.Round(imageLongSide / 60 * scale));
        int x0 = Math.Clamp((int)(area.X * scale), 0, w), y0 = Math.Clamp((int)(area.Y * scale), 0, h);
        int x1 = Math.Clamp((int)Math.Ceiling(area.Right * scale), 0, w), y1 = Math.Clamp((int)Math.Ceiling(area.Bottom * scale), 0, h);
        for (int by = y0; by < y1; by += block)
        for (int bx = x0; bx < x1; bx += block)
        {
            int ex = Math.Min(bx + block, x1), ey = Math.Min(by + block, y1);
            long sb = 0, sg = 0, sr = 0, sa = 0, n = 0;
            for (int y = by; y < ey; y++)
            for (int x = bx; x < ex; x++)
            {
                int i = (y * w + x) * 4;
                sb += px[i]; sg += px[i + 1]; sr += px[i + 2]; sa += px[i + 3]; n++;
            }
            if (n == 0) continue;
            byte b = (byte)(sb / n), g = (byte)(sg / n), r = (byte)(sr / n), al = (byte)(sa / n);
            for (int y = by; y < ey; y++)
            for (int x = bx; x < ex; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = al;
            }
        }
    }

    /// <summary>Draws vector marks (everything except pixelation, which is a pixel operation).</summary>
    public static void Draw(DrawingContext dc, Mark mark)
    {
        var brush = new SolidColorBrush(mark.Color);
        brush.Freeze();
        var pen = new Pen(brush, mark.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        switch (mark)
        {
            case PenMark p when p.Points.Count > 0:
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(p.Points[0], false, false);
                    if (p.Points.Count == 1) ctx.LineTo(p.Points[0] + new Vector(0.01, 0), true, true);
                    else ctx.PolyLineTo(p.Points.Skip(1).ToList(), true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                break;
            }
            case ArrowMark a:
            {
                dc.DrawLine(pen, a.From, a.To);
                var dir = a.To - a.From;
                if (dir.Length < 1) break;
                dir.Normalize();
                double head = Math.Max(a.Width * 4, 12);
                var normal = new Vector(-dir.Y, dir.X);
                var tip = a.To;
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(tip - dir * head + normal * head * 0.55, true, true);
                    ctx.LineTo(tip - dir * head - normal * head * 0.55, true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, pen, geo);
                break;
            }
            case BoxMark b:
                dc.DrawRoundedRectangle(null, pen, b.Area, mark.Width, mark.Width);
                break;
            case TextMark t:
            {
                var text = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    t.Size, brush, 1.0);
                dc.DrawText(text, t.At);
                break;
            }
            case RedactMark { Kind: RedactKind.Black } r:
                dc.DrawRectangle(Brushes.Black, null, r.Area);
                break;
        }
    }

    /// <summary>
    /// The full-resolution result: adjustments, pixelation, then marks, then the crop. Used for saving; the editor's
    /// preview runs the same steps on a smaller copy.
    /// </summary>
    public static BitmapSource Export(BitmapSource source, Adjustments adjust, IReadOnlyList<Mark> marks, Int32Rect? crop)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        byte[] px = Pixels(source);
        Adjust(px, adjust);
        foreach (var r in marks.OfType<RedactMark>().Where(m => m.Kind == RedactKind.Pixelate))
            Pixelate(px, w, h, r.Area, 1, Math.Max(w, h));
        var basis = FromPixels(px, w, h);

        BitmapSource composed = basis;
        var vector = marks.Where(m => m is not RedactMark { Kind: RedactKind.Pixelate }).ToList();
        if (vector.Count > 0)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(basis, new Rect(0, 0, w, h));
                foreach (var m in vector) Draw(dc, m);
            }
            var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var bgra = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
            composed = FromPixels(Pixels(bgra), w, h);
        }

        if (crop is { } c && (c.Width != w || c.Height != h))
        {
            var cropped = new CroppedBitmap(composed, c);
            composed = FromPixels(Pixels(cropped), c.Width, c.Height);
        }
        return composed;
    }
}
