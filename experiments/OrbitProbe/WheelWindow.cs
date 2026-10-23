using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using OrbitProbe.Native;

namespace OrbitProbe;

/// <summary>
/// Prototype radial wheel: topmost, never activates, per-pixel transparent, Per-Monitor-V2 aware.
/// Deliberately unpolished: it exists to validate behaviour, not to be the final design.
/// The whole disc is painted with non-zero alpha so the OS hit-tests (and OLE targets) every point on it;
/// the corners outside the circle are fully transparent and pass through to the window below.
/// </summary>
public sealed class WheelWindow : Window
{
    public const double Diameter = 300;
    private const double OuterR = 142, InnerR = 66, HubR = 58, Gap = 7, Corner = 10;

    public const int HitHub = -1, HitOutside = -2;

    private readonly Canvas _segments = new() { Width = Diameter, Height = Diameter, IsHitTestVisible = false };
    private readonly TextBlock _hubTitle, _hubText, _hubHint;
    private readonly Grid _root;
    private readonly ScaleTransform _scale = new(1, 1, Diameter / 2, Diameter / 2);
    private readonly List<Path> _paths = [];
    private readonly List<TextBlock> _labels = [];
    private readonly Brush _segmentFill, _labelBrush, _accent, _onAccent;
    private Win32.RECT _targetRect;
    private int _count, _highlight = HitOutside;

    public IntPtr Handle { get; }

    // Variant A (WPF AllowDrop) surfaces its events here; variant B uses NativeDropTarget instead.
    public event Action<System.Windows.IDataObject, Win32.POINT>? WpfDragEnter;
    public event Func<Win32.POINT, bool>? WpfDragOver; // returns "accept"
    public event Action? WpfDragLeave;
    public event Action<System.Windows.IDataObject, Win32.POINT>? WpfDrop;

    public WheelWindow(bool wpfDropTarget, bool? darkOverride = null)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = Diameter;
        Title = "OrbitProbe Wheel";

        bool dark = darkOverride ?? IsDarkMode();
        _accent = Frozen(new SolidColorBrush(AccentColor()));
        _segmentFill = Frozen(new SolidColorBrush(dark ? Color.FromRgb(0x2E, 0x2E, 0x32) : Color.FromRgb(0xFB, 0xFB, 0xFD)));
        // Opaque on purpose: the rounded-corner stroke overlaps the fill, so any alpha would show as a band.
        _onAccent = Frozen(new SolidColorBrush(Luminance(((SolidColorBrush)_accent).Color) > 0.55 ? Colors.Black : Colors.White));
        _labelBrush = Frozen(new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1C, 0x1C, 0x1E)));
        var plateFill = new SolidColorBrush(dark ? Color.FromArgb(0xB0, 0x18, 0x18, 0x1A) : Color.FromArgb(0x9C, 0xE4, 0xE4, 0xEA));
        var plateEdge = new SolidColorBrush(dark ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));

        var plate = new Ellipse { Width = Diameter - 4, Height = Diameter - 4, Fill = plateFill, Stroke = plateEdge, StrokeThickness = 1.5 };
        Canvas.SetLeft(plate, 2); Canvas.SetTop(plate, 2);

        var hub = new Ellipse { Width = HubR * 2, Height = HubR * 2, Fill = _segmentFill, IsHitTestVisible = false };
        _hubTitle = Label(10, FontWeights.SemiBold, 0.6);
        _hubText = Label(15, FontWeights.Bold, 1);
        _hubHint = Label(9.5, FontWeights.Normal, 0.55);
        var hubStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Width = HubR * 2 - 12 };
        hubStack.Children.Add(_hubTitle);
        hubStack.Children.Add(_hubText);
        hubStack.Children.Add(_hubHint);

        var plateCanvas = new Canvas { Width = Diameter, Height = Diameter };
        plateCanvas.Children.Add(plate);

        _root = new Grid { Width = Diameter, Height = Diameter, RenderTransform = _scale };
        _root.Children.Add(plateCanvas);
        _root.Children.Add(_segments);
        _root.Children.Add(hub);
        _root.Children.Add(hubStack);
        hub.HorizontalAlignment = HorizontalAlignment.Center;
        hub.VerticalAlignment = VerticalAlignment.Center;
        Content = _root;

        AllowDrop = wpfDropTarget;
        if (wpfDropTarget)
        {
            DragEnter += (_, e) => { WpfDragEnter?.Invoke(e.Data, ScreenPoint(e)); e.Effects = DragDropEffects.Copy; e.Handled = true; };
            DragOver += (_, e) => { e.Effects = WpfDragOver?.Invoke(ScreenPoint(e)) == true ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            DragLeave += (_, e) => { WpfDragLeave?.Invoke(); e.Handled = true; };
            Drop += (_, e) => { WpfDrop?.Invoke(e.Data, ScreenPoint(e)); e.Effects = DragDropEffects.Copy; e.Handled = true; };
        }

        SourceInitialized += (_, _) =>
        {
            long ex = Win32.GetWindowLongPtr(Handle, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(Handle, Win32.GWL_EXSTYLE, new IntPtr(ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
        };
        // Moving to a monitor with another DPI makes WPF apply the OS-suggested rect; re-assert ours.
        DpiChanged += (_, e) =>
        {
            ProbeLog.Info($"Wheel DPI changed {e.OldDpi.PixelsPerInchX:F0} → {e.NewDpi.PixelsPerInchX:F0}");
            Dispatcher.BeginInvoke(ApplyRect);
        };

        Handle = new WindowInteropHelper(this).EnsureHandle();
    }

    private Win32.POINT ScreenPoint(DragEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this));
        return new Win32.POINT((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    public int HighlightIndex => _highlight;
    public int ItemCount => _count;

    /// <summary>Shows the wheel centred on the cursor (clamped to the monitor work area). Returns the DPI used.</summary>
    public uint ShowAt(Win32.POINT cursor, bool animate)
    {
        IntPtr monitor = Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(monitor, ref info);
        if (Win32.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpi, out _) != 0) dpi = 96;

        int size = (int)Math.Round(Diameter * dpi / 96.0);
        var work = info.rcWork;
        int x = Math.Clamp(cursor.X - size / 2, work.Left, Math.Max(work.Left, work.Right - size));
        int y = Math.Clamp(cursor.Y - size / 2, work.Top, Math.Max(work.Top, work.Bottom - size));
        _targetRect = new Win32.RECT { Left = x, Top = y, Right = x + size, Bottom = y + size };

        ApplyRect();
        if (!IsVisible) Show();
        ApplyRect();

        if (animate && SystemParameters.ClientAreaAnimation)
        {
            var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(170);
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.35, 1, dur) { EasingFunction = ease });
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.35, 1, dur) { EasingFunction = ease });
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
        }
        else
        {
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _root.BeginAnimation(OpacityProperty, null);
            _scale.ScaleX = _scale.ScaleY = 1;
            _root.Opacity = 1;
        }
        return dpi;
    }

    private void ApplyRect()
    {
        var r = _targetRect;
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Win32.SWP_NOACTIVATE);
    }

    /// <summary>H-5 experiment: put the wheel directly beneath the shell drag image in the topmost z-band.</summary>
    public bool PlaceBelowDragImage()
    {
        IntPtr img = Win32.FindWindow("SysDragImage", null);
        if (img == IntPtr.Zero || !Win32.IsWindowVisible(img)) return false;
        return Win32.SetWindowPos(Handle, img, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public void SetContent(WheelMode mode, IReadOnlyList<WheelItem> items, string hubText)
    {
        _segments.Children.Clear();
        _paths.Clear();
        _labels.Clear();
        _count = items.Count;
        _highlight = HitOutside;
        _hubTitle.Text = mode.ToString().ToUpperInvariant();
        _hubText.Text = hubText;
        _hubHint.Text = "⇧ next mode";

        if (items.Count == 0)
        {
            var ring = Sector(0, 359.99, 0);
            ring.Opacity = 0.45;
            _segments.Children.Add(ring);
            return;
        }

        double step = 360.0 / items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            double mid = -90 + i * step; // first item at 12 o'clock, clockwise
            var path = Sector(mid - step / 2, mid + step / 2, items.Count == 1 ? 0 : Gap);
            _paths.Add(path);
            _segments.Children.Add(path);

            double rad = mid * Math.PI / 180, lr = (OuterR + InnerR) / 2;
            var label = Label(items.Count > 5 ? 12 : 13.5, FontWeights.Bold, 1);
            label.Text = items[i].Label;
            _labels.Add(label);
            label.Width = 90;
            label.Measure(new Size(90, double.PositiveInfinity));
            Canvas.SetLeft(label, Diameter / 2 + lr * Math.Cos(rad) - 45);
            Canvas.SetTop(label, Diameter / 2 + lr * Math.Sin(rad) - label.DesiredSize.Height / 2);
            _segments.Children.Add(label);
        }

        _segments.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(90)));
    }

    public void SetHubText(string text) => _hubText.Text = text;

    /// <summary>Renders the wheel offscreen over a sample backdrop (design review without screen capture).</summary>
    public void RenderToPng(string path, double scale)
    {
        _root.Measure(new Size(Diameter, Diameter));
        _root.Arrange(new Rect(0, 0, Diameter, Diameter));
        _root.UpdateLayout();
        int px = (int)Math.Ceiling(Diameter * scale) + 80;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xF0, 0x8A, 0x3C), Color.FromRgb(0x7A, 0x6C, 0xD8), 45), null,
                new Rect(0, 0, px / scale, px / scale));
            dc.DrawRectangle(new VisualBrush(_root), null, new Rect(40 / scale, 40 / scale, Diameter, Diameter));
        }
        var bmp = new RenderTargetBitmap(px, px, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    public void Highlight(int index)
    {
        if (index == _highlight) return;
        if (_highlight >= 0 && _highlight < _paths.Count)
        {
            _paths[_highlight].Fill = _paths[_highlight].Stroke = _segmentFill;
            _labels[_highlight].Foreground = _labelBrush;
        }
        _highlight = index;
        if (index >= 0 && index < _paths.Count)
        {
            _paths[index].Fill = _paths[index].Stroke = _accent;
            _labels[index].Foreground = _onAccent;
        }
    }

    /// <summary>Maps a physical screen point to a segment index, <see cref="HitHub"/> or <see cref="HitOutside"/>.</summary>
    public int HitTest(Win32.POINT screen)
    {
        if (!IsVisible || PresentationSource.FromVisual(this) is null) return HitOutside;
        Point p = PointFromScreen(new Point(screen.X, screen.Y));
        double dx = p.X - Diameter / 2, dy = p.Y - Diameter / 2, dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > OuterR + Gap) return HitOutside;
        if (dist < InnerR - Gap / 2 || _count == 0) return HitHub;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;           // -180..180, 0 = 3 o'clock
        double step = 360.0 / _count;
        double rel = ((angle + 90 + step / 2) % 360 + 360) % 360;     // 0 at 12 o'clock, clockwise
        return Math.Min(_count - 1, (int)(rel / step));
    }

    /// <summary>Annular sector with rounded corners: inset geometry stroked with a round join.</summary>
    private Path Sector(double startDeg, double endDeg, double gap)
    {
        double ro = OuterR - Corner / 2, ri = InnerR + Corner / 2;
        double gapOut = (gap / 2 + Corner / 2) / ro * 180 / Math.PI;
        double gapIn = (gap / 2 + Corner / 2) / ri * 180 / Math.PI;
        if (gap == 0) gapOut = gapIn = 0;

        Point P(double r, double deg) => new(Diameter / 2 + r * Math.Cos(deg * Math.PI / 180), Diameter / 2 + r * Math.Sin(deg * Math.PI / 180));
        double a0 = startDeg + gapOut, a1 = endDeg - gapOut, b0 = startDeg + gapIn, b1 = endDeg - gapIn;
        bool largeOut = a1 - a0 > 180, largeIn = b1 - b0 > 180;

        var fig = new PathFigure { StartPoint = P(ro, a0), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new ArcSegment(P(ro, a1), new Size(ro, ro), 0, largeOut, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(ri, b1), true));
        fig.Segments.Add(new ArcSegment(P(ri, b0), new Size(ri, ri), 0, largeIn, SweepDirection.Counterclockwise, true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        return new Path { Data = geo, Fill = _segmentFill, Stroke = _segmentFill, StrokeThickness = Corner, StrokeLineJoin = PenLineJoin.Round };
    }

    private TextBlock Label(double size, FontWeight weight, double opacity) => new()
    {
        FontSize = size, FontWeight = weight, Opacity = opacity, Foreground = _labelBrush,
        TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), IsHitTestVisible = false,
    };

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static Color AccentColor()
    {
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { return Color.FromRgb(0x5B, 0x5F, 0xC7); }
    }
}
