using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Flipr.UI.Trim;

/// <summary>
/// A timeline with start and end handles and a playhead. Values are fractions of the length (0–1). Drag a handle to
/// change the range; click or drag elsewhere to move the playhead.
/// </summary>
public sealed class TrimBar : FrameworkElement
{
    private const double HandleWidth = 14, TrackHeight = 44, MinGap = 0.01;
    private enum Part { None, Start, End, Playhead }
    private Part _dragging;

    public double Start { get; private set; }
    public double End { get; private set; } = 1;
    public double Playhead { get; private set; }

    /// <summary>A handle moved; the argument is where to show (the moved handle's position).</summary>
    public event Action<double>? RangeChanged;
    public event Action<double>? Seek;

    public TrimBar()
    {
        Height = TrackHeight + 8;
        Cursor = Cursors.Hand;
        Focusable = false;
    }

    /// <summary>Sets the kept range (fractions), e.g. for design review.</summary>
    public void SetRange(double start, double end)
    {
        Start = Math.Clamp(start, 0, 1);
        End = Math.Clamp(end, Start + MinGap, 1);
        Playhead = Start;
        InvalidateVisual();
    }

    public void SetPlayhead(double fraction)
    {
        Playhead = Math.Clamp(fraction, 0, 1);
        InvalidateVisual();
    }

    private Rect Track => new(HandleWidth, 4, Math.Max(1, ActualWidth - 2 * HandleWidth), TrackHeight);
    private double X(double f) => Track.Left + f * Track.Width;
    private double F(double x) => Math.Clamp((x - Track.Left) / Track.Width, 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        var t = Track;
        var fill = (Brush)FindResource("Orbit.SubtleFill");
        var accent = (Brush)FindResource("Orbit.Accent");
        var onAccent = (Brush)FindResource("Orbit.OnAccent");
        var text = (Brush)FindResource("Orbit.TextPrimary");

        dc.DrawRoundedRectangle(fill, null, t, 8, 8);
        // The kept range: a frame in the accent colour with grips at both ends (like Photos' trimmer).
        double x0 = X(Start), x1 = X(End);
        var kept = new Rect(x0 - HandleWidth, t.Top - 2, x1 - x0 + 2 * HandleWidth, t.Height + 4);
        var frame = new Pen(accent, 3);
        dc.DrawRoundedRectangle(null, frame, new Rect(x0, t.Top, Math.Max(0, x1 - x0), t.Height), 2, 2);
        dc.DrawRoundedRectangle(accent, null, new Rect(kept.Left, kept.Top, HandleWidth, kept.Height), 6, 6);
        dc.DrawRoundedRectangle(accent, null, new Rect(x1, kept.Top, HandleWidth, kept.Height), 6, 6);
        var grip = new Pen(onAccent, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double mid = t.Top + t.Height / 2;
        dc.DrawLine(grip, new Point(x0 - HandleWidth / 2, mid - 7), new Point(x0 - HandleWidth / 2, mid + 7));
        dc.DrawLine(grip, new Point(x1 + HandleWidth / 2, mid - 7), new Point(x1 + HandleWidth / 2, mid + 7));

        double px = X(Playhead);
        dc.DrawRoundedRectangle(text, null, new Rect(px - 1.5, t.Top - 4, 3, t.Height + 8), 1.5, 1.5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        double x = e.GetPosition(this).X;
        double ds = Math.Abs(x - (X(Start) - HandleWidth / 2)), de = Math.Abs(x - (X(End) + HandleWidth / 2));
        _dragging = ds < HandleWidth * 1.4 && ds <= de ? Part.Start : de < HandleWidth * 1.4 ? Part.End : Part.Playhead;
        CaptureMouse();
        Apply(x);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging != Part.None) Apply(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = Part.None;
        ReleaseMouseCapture();
    }

    private void Apply(double x)
    {
        switch (_dragging)
        {
            case Part.Start:
                Start = Math.Min(F(x + HandleWidth / 2), End - MinGap);
                Playhead = Start;
                RangeChanged?.Invoke(Start);
                break;
            case Part.End:
                End = Math.Max(F(x - HandleWidth / 2), Start + MinGap);
                Playhead = End;
                RangeChanged?.Invoke(End);
                break;
            case Part.Playhead:
                Playhead = Math.Clamp(F(x), Start, End);
                Seek?.Invoke(Playhead);
                break;
        }
        InvalidateVisual();
    }
}
