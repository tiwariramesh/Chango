using System.Windows;
using System.Windows.Media;

namespace Chango.UI.Editor;

/// <summary>
/// Draws the marks, the one being drawn, and the crop frame over the photo, in image pixel coordinates (the
/// surrounding Viewbox scales everything to the window).
/// </summary>
public sealed class EditorLayer : FrameworkElement
{
    public IReadOnlyList<Mark> Marks { get; set; } = [];
    public Mark? Pending { get; set; }
    public Rect? Crop { get; set; }
    public bool ShowCrop { get; set; }
    public Size ImageSize { get; set; }

    /// <summary>Screen-independent sizes: one "unit" is about one screen pixel at the current zoom.</summary>
    public double Unit { get; set; } = 1;

    public void Refresh() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) => ImageSize;

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var m in Marks)
        {
            if (m is RedactMark { Kind: RedactKind.Pixelate }) continue; // already in the preview pixels
            EditRenderer.Draw(dc, m);
        }
        if (Pending is RedactMark { Kind: RedactKind.Pixelate } p)
        {
            var dash = new Pen(Brushes.White, 2 * Unit) { DashStyle = new DashStyle([4, 3], 0) };
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), dash, p.Area);
        }
        else if (Pending is not null) EditRenderer.Draw(dc, Pending);

        if (ShowCrop && Crop is { } c)
        {
            var full = new RectangleGeometry(new Rect(ImageSize));
            var outside = Geometry.Combine(full, new RectangleGeometry(c), GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), null, outside);

            var thin = new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), 1 * Unit);
            for (int i = 1; i <= 2; i++) // rule of thirds
            {
                dc.DrawLine(thin, new Point(c.X + c.Width * i / 3, c.Y), new Point(c.X + c.Width * i / 3, c.Bottom));
                dc.DrawLine(thin, new Point(c.X, c.Y + c.Height * i / 3), new Point(c.Right, c.Y + c.Height * i / 3));
            }
            dc.DrawRectangle(null, new Pen(Brushes.White, 1.5 * Unit), c);

            // L-shaped corner handles, Photos-style.
            var handle = new Pen(Brushes.White, 4 * Unit) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            double len = Math.Min(22 * Unit, Math.Min(c.Width, c.Height) / 3);
            foreach (var (corner, dx, dy) in new[] { (c.TopLeft, 1, 1), (c.TopRight, -1, 1), (c.BottomLeft, 1, -1), (c.BottomRight, -1, -1) })
            {
                dc.DrawLine(handle, corner, corner + new Vector(dx * len, 0));
                dc.DrawLine(handle, corner, corner + new Vector(0, dy * len));
            }
        }
    }
}
