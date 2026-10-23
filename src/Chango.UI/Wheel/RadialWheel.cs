using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Chango.Core;
using Chango.Windows.Interop;
using Chango.Windows.Shell;
using static Chango.UI.Wheel.WheelGeometry;

namespace Chango.UI.Wheel;

/// <summary>
/// The Chango radial wheel: topmost, never activates, per-pixel transparent, Per-Monitor-V2 aware.
/// Everything is laid out in base units on a <see cref="WheelGeometry.Extent"/>-sized canvas inside a Viewbox, so the
/// window's physical size alone decides DPI scaling and the Compact/Standard/Large setting.
/// The whole disc has non-zero alpha so the OS (and OLE) hit-tests every point on it (probe finding F1).
/// </summary>
public sealed class RadialWheel : Window
{
    private sealed class SegmentVisual
    {
        public required Canvas Group;
        public required Path Shape;
        public required SolidColorBrush Fill;
        public required TextBlock Label;
        public TextBlock? Glyph;
        /// <summary>The "more options" dots under a group label.</summary>
        public TextBlock? Dots;
        public required TranslateTransform Lift;
        public required double Angle;
    }

    private readonly Grid _root;
    private readonly ScaleTransform _bloom = new(1, 1, Extent / 2, Extent / 2);
    private readonly Canvas _segments = new() { Width = Extent, Height = Extent, IsHitTestVisible = false };
    private readonly RotateTransform _segmentsSpin = new(0, Extent / 2, Extent / 2);
    private readonly Ellipse _base, _tint, _rim, _hub;
    private readonly Image _backdrop = new() { Stretch = Stretch.Fill };
    private readonly DropShadowEffect _shadow = new() { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Color = Colors.Black };
    private readonly TextBlock _hubMode, _hubValue, _hubDetail;
    private readonly Rectangle[] _modeDots = new Rectangle[3];
    private readonly List<SegmentVisual> _visuals = [];
    private Win32.RECT _rect;
    private int _highlight = HitOutside;
    private Color _segmentColor, _segmentText, _accent, _onAccent;
    private double _segmentOpacity;

    public RadialWheel()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = Extent;
        Title = "Chango";
        UseLayoutRounding = true;

        double d = Disc, m = WheelGeometry.Margin;
        _base = new Ellipse { Width = d, Height = d, Effect = _shadow };
        var backdropHost = new Border { Width = d, Height = d, Clip = new EllipseGeometry(new Point(d / 2, d / 2), d / 2, d / 2), Child = _backdrop };
        _backdrop.Effect = new BlurEffect { Radius = 22, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        _backdrop.Margin = new Thickness(-m); // the snapshot covers the whole window; only the disc part is visible
        _backdrop.Width = _backdrop.Height = Extent;
        RenderOptions.SetBitmapScalingMode(_backdrop, BitmapScalingMode.LowQuality);
        _tint = new Ellipse { Width = d, Height = d };
        _rim = new Ellipse { Width = d, Height = d, StrokeThickness = 2 };
        _hub = new Ellipse { Width = HubR * 2, Height = HubR * 2, StrokeThickness = 2 };

        var disc = new Canvas { Width = Extent, Height = Extent };
        foreach (FrameworkElement e in new FrameworkElement[] { _base, backdropHost, _tint, _rim })
        {
            System.Windows.Controls.Canvas.SetLeft(e, m);
            System.Windows.Controls.Canvas.SetTop(e, m);
            disc.Children.Add(e);
        }
        System.Windows.Controls.Canvas.SetLeft(_hub, Extent / 2 - HubR);
        System.Windows.Controls.Canvas.SetTop(_hub, Extent / 2 - HubR);

        _segments.RenderTransform = _segmentsSpin;

        _hubMode = HubText(10, FontWeights.SemiBold, 0.62);
        _hubMode.Margin = new Thickness(0, 0, 0, 1);
        _hubValue = HubText(16, FontWeights.SemiBold, 1);
        _hubDetail = HubText(10.5, FontWeights.Normal, 0.62);
        // Two lines at most: anything longer ends in "…" instead of running into the mode dots.
        _hubDetail.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _hubDetail.LineHeight = 13;
        _hubDetail.MaxHeight = 26;
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        for (int i = 0; i < 3; i++)
        {
            _modeDots[i] = new Rectangle { Width = 5, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Margin = new Thickness(2, 0, 2, 0) };
            dots.Children.Add(_modeDots[i]);
        }
        var hubStack = new StackPanel
        {
            Width = HubR * 2 - 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        hubStack.Children.Add(_hubMode);
        hubStack.Children.Add(_hubValue);
        hubStack.Children.Add(_hubDetail);
        hubStack.Children.Add(dots);

        _root = new Grid { Width = Extent, Height = Extent, RenderTransform = _bloom };
        _root.Children.Add(disc);
        _root.Children.Add(_segments);
        var hubLayer = new Canvas { Width = Extent, Height = Extent, IsHitTestVisible = false };
        hubLayer.Children.Add(_hub);
        _root.Children.Add(hubLayer);
        _root.Children.Add(hubStack);

        Content = new Viewbox { Stretch = Stretch.Fill, Child = _root };

        SourceInitialized += (_, _) => Win32.AddExStyle(Handle, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ApplyRect); // WPF applies the OS-suggested rect; keep ours
        Handle = new WindowInteropHelper(this).EnsureHandle();
        ApplyTheme();
    }

    public IntPtr Handle { get; }
    public int Highlight => _highlight;
    public int ItemCount => _visuals.Count;

    // ------------------------------------------------------------------ appearance

    /// <summary>Re-reads design tokens (called on every open, so theme changes apply without restarting).</summary>
    public void ApplyTheme()
    {
        bool hc = SystemParameters.HighContrast;
        _segmentColor = hc ? SystemColors.WindowColor : ThemeManager.Token("WheelSegment");
        _segmentText = hc ? SystemColors.WindowTextColor : ThemeManager.Token("WheelSegmentText");
        _segmentOpacity = hc ? 1 : ThemeManager.Number("WheelSegmentOpacity");
        _accent = hc ? SystemColors.HighlightColor : ThemeManager.Token("Accent");
        _onAccent = hc ? SystemColors.HighlightTextColor : ThemeManager.Token("OnAccent");

        _base.Fill = Frozen(new SolidColorBrush(hc ? SystemColors.WindowColor : ThemeManager.Token("WheelBase")));
        _shadow.Opacity = hc ? 0 : ThemeManager.Number("WheelShadowOpacity");
        _tint.Fill = Frozen(new SolidColorBrush(hc ? Colors.Transparent : ThemeManager.Token("WheelTint")));
        _rim.Stroke = hc
            ? Frozen(new SolidColorBrush(SystemColors.WindowTextColor))
            : Frozen(new LinearGradientBrush(ThemeManager.Token("WheelRimTop"), ThemeManager.Token("WheelRimBottom"), 90));
        _hub.Fill = Frozen(new SolidColorBrush(hc ? SystemColors.WindowColor : ThemeManager.Token("WheelHub")));
        _hub.Stroke = Frozen(new SolidColorBrush(hc ? SystemColors.WindowTextColor : Color.FromArgb(0x99, _accent.R, _accent.G, _accent.B)));
        var text = Frozen(new SolidColorBrush(_segmentText));
        _hubValue.Foreground = _hubDetail.Foreground = text;
        _hubMode.Foreground = hc ? text : Frozen(new SolidColorBrush(ThemeManager.Token("AccentText")));
        foreach (var v in _visuals) Paint(v, _highlight >= 0 && _highlight < _visuals.Count ? _visuals[_highlight] : null);
    }

    /// <summary>Replaces the segments. <paramref name="modeIndex"/> drives the three-dot indicator.</summary>
    public void SetContent(OperationMode mode, IReadOnlyList<OperationDescriptor> items, string value, string detail, bool animate)
    {
        _segments.Children.Clear();
        _visuals.Clear();
        _highlight = HitOutside;
        _hubMode.Text = mode.ToString().ToUpperInvariant();
        SetHub(value, detail);
        var dotOn = Frozen(new SolidColorBrush(_accent));
        var dotOff = Frozen(new SolidColorBrush(Color.FromArgb(0x55, _segmentText.R, _segmentText.G, _segmentText.B)));
        for (int i = 0; i < 3; i++)
        {
            _modeDots[i].Fill = i == (int)mode ? dotOn : dotOff;
            _modeDots[i].Width = i == (int)mode ? 14 : 5;
        }

        if (items.Count == 0)
        {
            var ring = new Path { Data = Sector(0, 359.999, 0), Opacity = _segmentOpacity * 0.55,
                Fill = Frozen(new SolidColorBrush(_segmentColor)), Stroke = Frozen(new SolidColorBrush(_segmentColor)),
                StrokeThickness = Corner, StrokeLineJoin = PenLineJoin.Round };
            _segments.Children.Add(ring);
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double mid = MidAngle(i, items.Count), step = Step(items.Count);
            var fill = new SolidColorBrush(_segmentColor);
            var shape = new Path
            {
                Data = Sector(mid - step / 2, mid + step / 2, items.Count == 1 ? 0 : Gap),
                Fill = fill, Stroke = fill, StrokeThickness = Corner, StrokeLineJoin = PenLineJoin.Round,
                // Opacity on the element (not the brush): fill and rounded stroke overlap without banding.
                Opacity = _segmentOpacity,
            };

            double rad = mid * Math.PI / 180;
            var center = new Point(Extent / 2 + LabelR * Math.Cos(rad), Extent / 2 + LabelR * Math.Sin(rad));
            var label = new TextBlock
            {
                Text = item.Label, // format labels are already upper-case ("PNG"); presets keep their case ("720p")
                FontFamily = (FontFamily)FindResource("Orbit.Font.Display"),
                FontSize = item.Glyph is null ? (items.Count > 6 ? 12.5 : 13.5) : 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            TextBlock? glyph = null;
            var stack = new StackPanel();
            if (item.Glyph is not null)
            {
                glyph = new TextBlock
                {
                    Text = item.Glyph, FontFamily = (FontFamily)FindResource("Orbit.Font.Icons"), FontSize = 19,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = item.MirrorGlyph ? new ScaleTransform(-1, 1) : Transform.Identity,
                };
                stack.Children.Add(glyph);
            }
            stack.Children.Add(label);
            TextBlock? dots = null;
            if (item.IsGroupHeader)
            {
                dots = new TextBlock
                {
                    Text = "\uE710", FontFamily = (FontFamily)FindResource("Orbit.Font.Icons"), FontSize = 9, FontWeight = FontWeights.Bold,
                    Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
                };
                stack.Children.Add(dots);
            }
            center = FitLabel(stack, label, center, items.Count > 5 ? 64 : 78, items.Count > 4 ? 5 : 10);
            System.Windows.Controls.Canvas.SetLeft(stack, center.X - stack.DesiredSize.Width / 2);
            System.Windows.Controls.Canvas.SetTop(stack, center.Y - stack.DesiredSize.Height / 2);

            var lift = new TranslateTransform();
            var group = new Canvas { Width = Extent, Height = Extent, RenderTransform = lift };
            group.Children.Add(shape);
            group.Children.Add(stack);
            _segments.Children.Add(group);

            var visual = new SegmentVisual { Group = group, Shape = shape, Fill = fill, Label = label, Glyph = glyph, Dots = dots, Lift = lift, Angle = mid };
            _visuals.Add(visual);
            Paint(visual, null);
        }

        if (animate && !ThemeManager.Current.ReduceMotion)
        {
            var d = TimeSpan.FromMilliseconds(140);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _segmentsSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-14, 0, d) { EasingFunction = ease });
            _segments.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, d) { EasingFunction = ease });
        }
        else
        {
            _segmentsSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            _segments.BeginAnimation(OpacityProperty, null);
            _segmentsSpin.Angle = 0;
            _segments.Opacity = 1;
        }
    }

    public void SetHub(string value, string detail)
    {
        _hubValue.Text = value;
        _hubValue.FontSize = value.Length > 11 ? 13 : 16; // long labels ("Show in folder") fit the hub
        _hubDetail.TextWrapping = detail.Length > 18 ? TextWrapping.Wrap : TextWrapping.NoWrap; // two short lines beat an ellipsis
        _hubDetail.Text = detail;
    }

    /// <param name="animate">False for offscreen renders (guide, film): a colour fade wouldn't have started yet.</param>
    public void SetHighlight(int index, bool animate = true)
    {
        if (index == _highlight) return;
        var previous = _highlight >= 0 && _highlight < _visuals.Count ? _visuals[_highlight] : null;
        _highlight = index;
        var current = index >= 0 && index < _visuals.Count ? _visuals[index] : null;
        if (previous is not null) Paint(previous, null, animate);
        if (current is not null) Paint(current, current, animate);
    }

    private void Paint(SegmentVisual v, SegmentVisual? highlighted, bool animate = false)
    {
        bool on = ReferenceEquals(v, highlighted);
        Color fill = on ? _accent : _segmentColor;
        double opacity = on ? 1 : _segmentOpacity;
        double rad = v.Angle * Math.PI / 180, lift = on && !ThemeManager.Current.ReduceMotion ? HoverLift : 0;
        if (animate && !ThemeManager.Current.ReduceMotion)
        {
            var dur = TimeSpan.FromMilliseconds(90);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            v.Fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fill, dur) { EasingFunction = ease });
            v.Shape.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, dur) { EasingFunction = ease });
            v.Lift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(lift * Math.Cos(rad), dur) { EasingFunction = ease });
            v.Lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lift * Math.Sin(rad), dur) { EasingFunction = ease });
        }
        else
        {
            // Apply immediately: a zero-length animation would only take effect on the next frame.
            v.Fill.BeginAnimation(SolidColorBrush.ColorProperty, null);
            v.Shape.BeginAnimation(OpacityProperty, null);
            v.Lift.BeginAnimation(TranslateTransform.XProperty, null);
            v.Lift.BeginAnimation(TranslateTransform.YProperty, null);
            v.Fill.Color = fill;
            v.Shape.Opacity = opacity;
            v.Lift.X = lift * Math.Cos(rad);
            v.Lift.Y = lift * Math.Sin(rad);
        }
        var text = Frozen(new SolidColorBrush(on ? _onAccent : _segmentText));
        v.Label.Foreground = text;
        if (v.Glyph is not null) v.Glyph.Foreground = text;
        if (v.Dots is not null) v.Dots.Foreground = text;
    }

    // ------------------------------------------------------------------ placement & lifecycle

    /// <summary>
    /// Opens centred on <paramref name="cursor"/> (physical px), clamped so the disc stays inside the work area.
    /// Captures the frosted backdrop first (the wheel isn't visible yet, and BitBlt without CAPTUREBLT skips the drag image).
    /// </summary>
    public void Open(Win32.POINT cursor, double sizeFactor, bool animate)
    {
        StopAnimations();
        var monitor = ScreenInfo.MonitorAt(cursor);
        double scale = monitor.Scale * sizeFactor;
        int size = (int)Math.Round(Extent * scale);
        int discR = (int)Math.Ceiling(DiscR * scale), pad = (int)(4 * scale);
        var work = monitor.WorkArea;
        int cx = Math.Clamp(cursor.X, work.Left + discR + pad, Math.Max(work.Left + discR + pad, work.Right - discR - pad));
        int cy = Math.Clamp(cursor.Y, work.Top + discR + pad, Math.Max(work.Top + discR + pad, work.Bottom - discR - pad));
        _rect = new Win32.RECT(cx - size / 2, cy - size / 2, cx - size / 2 + size, cy - size / 2 + size);

        _backdrop.Source = SystemParameters.HighContrast ? null : ScreenInfo.Capture(_rect);
        ApplyTheme();
        ApplyRect();
        if (!IsVisible) Show();
        ApplyRect();

        if (animate && !ThemeManager.Current.ReduceMotion)
        {
            var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            var bloom = TimeSpan.FromMilliseconds(170);
            _bloom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.4, 1, bloom) { EasingFunction = ease });
            _bloom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, 1, bloom) { EasingFunction = ease });
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
        }
        else if (animate)
        {
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80)));
        }
    }

    /// <summary>Maps a physical screen point to a segment, <see cref="WheelGeometry.HitHub"/> or <see cref="WheelGeometry.HitOutside"/>.</summary>
    public int HitTest(Win32.POINT screen)
    {
        if (!IsVisible || _rect.Width <= 0) return HitOutside;
        double k = Extent / _rect.Width;
        double dx = (screen.X - _rect.Left) * k - Extent / 2, dy = (screen.Y - _rect.Top) * k - Extent / 2;
        return WheelGeometry.HitTest(dx, dy, _visuals.Count);
    }

    /// <summary>Closes the wheel. A confirmed drop gets a short "accept" pop; cancellation disappears instantly.</summary>
    public void Dismiss(bool confirmed)
    {
        if (!IsVisible) return;
        if (!confirmed || ThemeManager.Current.ReduceMotion) { HideNow(); return; }
        var d = TimeSpan.FromMilliseconds(90);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        _bloom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, d) { EasingFunction = ease });
        _bloom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, d) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, d) { EasingFunction = ease };
        fade.Completed += (_, _) => { if (_root.Opacity < 0.05) HideNow(); };
        _root.BeginAnimation(OpacityProperty, fade);
    }

    private void HideNow()
    {
        Hide();
        StopAnimations();
        _backdrop.Source = null; // don't keep a screen snapshot around
    }

    private void StopAnimations()
    {
        _bloom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _bloom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _root.BeginAnimation(OpacityProperty, null);
        _bloom.ScaleX = _bloom.ScaleY = 1;
        _root.Opacity = 1;
    }

    /// <summary>Shows and hides the window off-screen once so the first real open is instant (feasibility R4).</summary>
    public void Prewarm()
    {
        _rect = new Win32.RECT(-32000, -32000, -32000 + 64, -32000 + 64);
        Opacity = 0;
        ApplyRect();
        Show();
        UpdateLayout();
        Hide();
        Opacity = 1;
    }

    private void ApplyRect() =>
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, _rect.Left, _rect.Top, _rect.Width, _rect.Height, Win32.SWP_NOACTIVATE);

    // ------------------------------------------------------------------ design review

    /// <summary>Renders the wheel offscreen over a sample wallpaper (used by --render-ui; no screen capture).</summary>
    /// <param name="wallpaperBehind">False renders the wheel alone on transparency (wallpaper only shows through the frost).</param>
    public BitmapSource RenderPreview(ImageSource wallpaper, double scale, bool wallpaperBehind = true)
    {
        _backdrop.Source = wallpaper;
        _root.Measure(new Size(Extent, Extent));
        _root.Arrange(new Rect(0, 0, Extent, Extent));
        _root.UpdateLayout();
        int px = (int)Math.Ceiling(Extent * scale);
        var bmp = new RenderTargetBitmap(px, px, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        if (wallpaperBehind)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawImage(wallpaper, new Rect(0, 0, Extent, Extent));
            bmp.Render(dv);
        }
        bmp.Render(_root);
        bmp.Freeze();
        _backdrop.Source = null;
        return bmp;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Annular sector with rounded corners: inset geometry stroked with a round join.</summary>
    internal static Geometry Sector(double startDeg, double endDeg, double gap)
    {
        double ro = OuterR - Corner / 2, ri = InnerR + Corner / 2;
        double gapOut = gap == 0 ? 0 : (gap / 2 + Corner / 2) / ro * 180 / Math.PI;
        double gapIn = gap == 0 ? 0 : (gap / 2 + Corner / 2) / ri * 180 / Math.PI;
        Point P(double r, double deg) => new(Extent / 2 + r * Math.Cos(deg * Math.PI / 180), Extent / 2 + r * Math.Sin(deg * Math.PI / 180));
        double a0 = startDeg + gapOut, a1 = endDeg - gapOut, b0 = startDeg + gapIn, b1 = endDeg - gapIn;

        var fig = new PathFigure { StartPoint = P(ro, a0), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new ArcSegment(P(ro, a1), new Size(ro, ro), 0, a1 - a0 > 180, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(ri, b1), true));
        fig.Segments.Add(new ArcSegment(P(ri, b0), new Size(ri, ri), 0, b1 - b0 > 180, SweepDirection.Counterclockwise, true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        return geo;
    }

    /// <summary>
    /// Keeps a segment's label inside the ring. A long label on a wide segment ("Show in folder" on a three-item wheel)
    /// would otherwise run past the segment's edge and vanish against the rim. Tries the widest wrap first, nudging the
    /// label up to <paramref name="maxShift"/> units from its natural centre, and only then wraps tighter.
    /// Returns the centre to place the stack at.
    /// </summary>
    private static Point FitLabel(StackPanel stack, TextBlock label, Point center, double maxWidth, double maxShift)
    {
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var shifts = new List<Vector>();
        for (double x = -maxShift; x <= maxShift; x += 1)
            for (double y = -maxShift; y <= maxShift; y += 1)
                if (x * x + y * y <= maxShift * maxShift) shifts.Add(new Vector(x, y));
        shifts.Sort((a, b) => a.LengthSquared.CompareTo(b.LengthSquared));

        // Narrower wraps first; if even that fails, a slightly smaller font. Never wrap inside a word ("Downloa/ds").
        double baseSize = label.FontSize;
        foreach (double scale in new[] { 1.0, 0.93, 0.86, 0.8 })
        {
            label.FontSize = baseSize * scale;
            double minWidth = Math.Max(40, WidestWord(label));
            for (double w = maxWidth; w >= minWidth; w -= 4)
            {
                var fit = TryFit(w);
                if (fit is { } c) return c;
            }
            if (TryFit(Math.Ceiling(minWidth)) is { } tight) return tight;
        }
        label.FontSize = baseSize;
        label.MaxWidth = maxWidth; // nothing fits (very long text): keep the original layout
        label.InvalidateMeasure();
        stack.InvalidateMeasure();
        stack.Measure(inf);
        return center;

        Point? TryFit(double w)
        {
            label.MaxWidth = w;
            // Not in the visual tree yet, so a repeat Measure with the same size would return the cached result.
            label.InvalidateMeasure();
            stack.InvalidateMeasure();
            stack.Measure(inf);
            // Test the label's own box (between the glyph and any group dots), not the whole stack's.
            double lw = label.DesiredSize.Width, lh = label.DesiredSize.Height, above = 0;
            foreach (UIElement child in stack.Children) { if (child == label) break; above += child.DesiredSize.Height; }
            double below = -stack.DesiredSize.Height / 2 + above + lh / 2;
            foreach (var s in shifts)
            {
                var c = center + s;
                if (LabelFits(c.X - Extent / 2, c.Y + below - Extent / 2, lw, lh)) return c;
            }
            return null;
        }
    }

    /// <summary>Width of the label's longest word at its current font, so wrapping never splits a word.</summary>
    private static double WidestWord(TextBlock label)
    {
        double widest = 0;
        foreach (var word in label.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var probe = new TextBlock { Text = word, FontFamily = label.FontFamily, FontSize = label.FontSize, FontWeight = label.FontWeight };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }
        return widest;
    }

    private TextBlock HubText(double size, FontWeight weight, double opacity) => new()
    {
        FontSize = size, FontWeight = weight, Opacity = opacity, TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = (FontFamily)FindResource("Orbit.Font.Display"),
    };

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
