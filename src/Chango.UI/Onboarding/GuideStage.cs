using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Chango.Core;
using Chango.UI.Wheel;

namespace Chango.UI.Onboarding;

public enum GuideScene { Hero, Drag, Release, Modes, Groups, Keyboard, Customize }

/// <summary>
/// The guide's animated demonstrations: a little desktop with a Pictures window, a pointer, the real Chango
/// wheel and notification (rendered by the app), and the keys being pressed. Every scene is a pure function of time
/// (<see cref="Apply"/>), so it plays live in the guide and exports frame-exact GIFs for the README.
/// </summary>
public sealed class GuideStage : Canvas
{
    public const double W = 600, H = 320;
    private const double WheelSize = 236;
    private static readonly Point Center = new(430, 150), Rest = new(320, 300), TileA = new(90, 106);

    private readonly IGuideAssets _assets;
    private readonly string _triggerKey;
    private readonly Canvas _content = new() { Width = W, Height = H };
    private readonly Border _window, _settings, _save;
    private readonly Settings.WheelEditor _editor = new() { Width = EditorSize, Height = EditorSize, IsHitTestVisible = false };
    private const double EditorSize = 196;
    private readonly Border _tileA, _tileB, _tileC, _ghost;
    private readonly TextBlock _tileBName, _tileCName;
    private readonly Image _wheel = new() { Width = WheelSize, Height = WheelSize };
    private readonly ScaleTransform _wheelScale = new();
    private readonly RotateTransform _wheelSpin = new();
    private readonly Image _toast = new() { Width = 236 };
    private readonly TranslateTransform _toastSlide = new();
    private readonly System.Windows.Shapes.Path _cursor;
    private readonly ScaleTransform _cursorScale = new();
    private readonly System.Windows.Shapes.Ellipse _ripple = new() { StrokeThickness = 2 };
    private readonly System.Windows.Shapes.Path _dwell = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly StackPanel _keys = new() { Orientation = Orientation.Horizontal };
    private readonly Border _caption = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 6) };
    private readonly TextBlock _captionText = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold };
    private readonly Dictionary<string, Border> _keyCaps = [];
    private readonly Dictionary<string, WheelImage> _shots = [];
    private readonly Dictionary<string, BitmapSource> _toasts = [];

    /// <summary>Show the guide's caption chips (off for Store art, which has its own headlines).</summary>
    public bool Captions { get; set; } = true;

    /// <summary>Show the pointer and the dragged file (Store art hides them where they would cover the wheel's labels).</summary>
    public bool Pointer { get; set; } = true;

    /// <param name="flatBackground">Plain background instead of the soft gradient (GIF export: gradients bloat GIFs).</param>
    public GuideStage(IGuideAssets assets, string triggerKey, bool flatBackground = false)
    {
        _assets = assets;
        _triggerKey = triggerKey;
        Width = W;
        Height = H;
        ClipToBounds = true;

        var bg = new Border
        {
            Width = W, Height = H, CornerRadius = new CornerRadius(12),
            Background = flatBackground
                ? new SolidColorBrush(ThemeManager.Token("SurfaceAlt"))
                : new LinearGradientBrush(ThemeManager.Token("SurfaceAlt"), ThemeManager.Token("StageTint"), 30),
        };
        Children.Add(bg);
        Children.Add(_content);

        // A small File Explorer-like "Pictures" window.
        _window = new Border
        {
            Width = 252, Height = 270, CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.FindResource("Chango.Surface"),
            BorderBrush = (Brush)Application.Current.FindResource("Chango.Stroke"), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.12 },
        };
        var bar = new DockPanel { Margin = new Thickness(12, 10, 12, 0), Height = 18 };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var _ in Enumerable.Range(0, 3))
            dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 5, 0), Fill = (Brush)Application.Current.FindResource("Chango.StrokeStrong") });
        bar.Children.Add(dots);
        bar.Children.Add(new TextBlock { Text = "Pictures", FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("Chango.TextPrimary") });
        _window.Child = new StackPanel { Children = { bar } };
        Place(_window, 22, 24);
        _content.Children.Add(_window);

        // A mini Settings → Wheel page for the customization scene: the real wheel editor, plus Save.
        var panel = new Canvas { Width = 252, Height = 270 };
        var title = new TextBlock { Text = "Settings  ›  Wheel", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("Chango.TextPrimary") };
        Place(title, 16, 14);
        panel.Children.Add(title);
        Place(_editor, (252 - EditorSize) / 2, 36);
        panel.Children.Add(_editor);
        _save = new Border
        {
            Width = 96, Height = 28, CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.FindResource("Chango.Accent"),
            Child = new TextBlock { Text = "Save", FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("Chango.OnAccent") },
        };
        Place(_save, 252 - 96 - 16, 270 - 28 - 14);
        panel.Children.Add(_save);
        _settings = new Border
        {
            Width = 252, Height = 270, CornerRadius = new CornerRadius(10), Child = panel,
            Background = (Brush)Application.Current.FindResource("Chango.Surface"),
            BorderBrush = (Brush)Application.Current.FindResource("Chango.Stroke"), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.12 },
        };
        Place(_settings, 22, 24);
        _content.Children.Add(_settings);

        _tileA = Tile("photo.png", out _);
        _tileB = Tile("photo.jpg", out _tileBName);
        _tileC = Tile("photo (compressed).jpg", out _tileCName);
        Place(_tileA, 40, 58);
        Place(_tileB, 148, 58);
        Place(_tileC, 40, 170);
        _content.Children.Add(_tileA);
        _content.Children.Add(_tileB);
        _content.Children.Add(_tileC);

        _wheel.RenderTransformOrigin = new Point(0.5, 0.5);
        _wheel.RenderTransform = new TransformGroup { Children = { _wheelSpin, _wheelScale } };
        Place(_wheel, Center.X - WheelSize / 2, Center.Y - WheelSize / 2);
        _content.Children.Add(_wheel);

        // The drag image floats above everything, like Windows' own.
        _ghost = Tile("photo.png", out _, ghost: true);
        _content.Children.Add(_ghost);

        _toast.RenderTransform = _toastSlide;
        Place(_toast, W - 248, H - 96);
        _content.Children.Add(_toast);

        foreach (var k in new[] { triggerKey, "Ctrl", "Alt", "Space" }.Distinct())
        {
            var cap = KeyCap(k);
            _keyCaps[k] = cap;
            _keys.Children.Add(cap);
        }
        _content.Children.Add(_keys);

        // A caption chip above the wheel that says what the next click does (keyboard scene).
        _caption.Background = (Brush)Application.Current.FindResource("Chango.Accent");
        _captionText.Foreground = (Brush)Application.Current.FindResource("Chango.OnAccent");
        _caption.Child = _captionText;
        _content.Children.Add(_caption);

        var accent = (Brush)Application.Current.FindResource("Chango.Accent");
        _ripple.Stroke = accent;
        _dwell.Stroke = accent;
        _content.Children.Add(_ripple);
        _content.Children.Add(_dwell);

        // A standard arrow pointer (white with a dark outline, like Windows).
        _cursor = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.2,13.2 L7,19.5 L9.6,18.4 L6.9,12.2 L12.2,12.2 Z"),
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.35, 1.35), _cursorScale } },
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.35 },
        };
        _content.Children.Add(_cursor);
    }

    public static double DurationOf(GuideScene s) => s switch
    {
        GuideScene.Hero => 9.4, GuideScene.Drag => 5.4, GuideScene.Release => 6.2,
        GuideScene.Modes => 5.4, GuideScene.Groups => 7.2, GuideScene.Customize => 9.4, _ => 10.6,
    };

    // ------------------------------------------------------------------ building blocks

    private static void Place(UIElement e, double x, double y) { SetLeft(e, x); SetTop(e, y); }

    private static Border Tile(string name, out TextBlock label, bool ghost = false)
    {
        label = new TextBlock
        {
            Text = name, FontSize = 10.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 96,
            Foreground = (Brush)Application.Current.FindResource("Chango.TextPrimary"), Margin = new Thickness(0, 5, 0, 0),
        };
        var thumb = new Border
        {
            Width = ghost ? 40 : 60, Height = ghost ? 40 : 60, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new ImageBrush(SamplePhoto.Value) { Stretch = Stretch.UniformToFill },
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderThickness = new Thickness(1),
        };
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = ghost ? thumb : new StackPanel { Children = { thumb, label } },
            Padding = ghost ? new Thickness(0) : new Thickness(4, 6, 4, 6), Width = ghost ? double.NaN : 104,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
            Effect = ghost ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Opacity = 0.25 } : null,
        };
    }

    private static Border KeyCap(string text) => new()
    {
        MinWidth = text == "Space" ? 120 : 58, Height = 34, Margin = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1, 1, 1, 3), RenderTransform = new TranslateTransform(),
        Child = new TextBlock { Text = text == "Shift" ? "⇧ Shift" : text, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) },
    };

    private static readonly Lazy<BitmapSource> SamplePhoto = new(() => Photo(120));

    /// <summary>A sunset "photo" for the demo files and the practice step (drawn, so nothing is bundled).</summary>
    public static BitmapSource Photo(int size)
    {
        double k = size / 120.0;
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(k, k));
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x3A, 0x5B, 0x9C), Color.FromRgb(0xF6, 0xA1, 0x5C), 90), null, new Rect(0, 0, 120, 120));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x9A)), null, new Point(78, 66), 18, 18);
            var hills = Geometry.Parse("M0,86 C30,70 50,74 70,84 C90,94 104,80 120,76 L120,120 L0,120 Z");
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x2E, 0x3A, 0x4F)), null, hills);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        bmp.Freeze();
        return bmp;
    }

    private WheelImage Shot(string key, WheelShot shot)
    {
        if (!_shots.TryGetValue(key, out var img)) _shots[key] = img = _assets.Wheel(shot);
        return img;
    }

    private BitmapSource ToastImage(string title, string detail)
    {
        string key = title + "|" + detail;
        if (!_toasts.TryGetValue(key, out var img)) _toasts[key] = img = _assets.Toast(title, detail);
        return img;
    }

    private static readonly string[] PhotoFiles = ["photo.png"];
    private WheelImage ConvertIdle => Shot("ci", new(OperationMode.Convert, PhotoFiles, WheelGeometry.HitOutside, "2.4 MB", "photo.png"));
    private WheelImage ConvertJpg => Shot("cj", new(OperationMode.Convert, PhotoFiles, IndexOf(ConvertIdle, "JPG"), "JPG", "Convert to JPG"));
    private WheelImage ToolsIdle => Shot("ti", new(OperationMode.Tools, PhotoFiles, WheelGeometry.HitOutside, "2.4 MB", "photo.png"));
    private WheelImage ToolsCompress => Shot("tc", new(OperationMode.Tools, PhotoFiles, IndexOf(ToolsIdle, "Compress"), "Compress", "Hold for options"));
    private WheelImage CompressGroup => Shot("cg", new(OperationMode.Tools, PhotoFiles, WheelGeometry.HitOutside, "Compress", "Rest on the centre to go back", "image.compress"));
    private WheelImage Compress1Mb => Shot("c1", new(OperationMode.Tools, PhotoFiles, IndexOf(CompressGroup, "1 MB"), "1 MB", "Compress to under 1 MB", "image.compress"));
    private WheelImage ActionsIdle => Shot("ai", new(OperationMode.Actions, PhotoFiles, WheelGeometry.HitOutside, "2.4 MB", "photo.png"));
    // Keyboard flow: the centre says where a click leads.
    private WheelImage ConvertClick => Shot("kc", new(OperationMode.Convert, PhotoFiles, WheelGeometry.HitHub, "2.4 MB", "Click for Tools"));
    private WheelImage ToolsClick => Shot("kt", new(OperationMode.Tools, PhotoFiles, WheelGeometry.HitHub, "2.4 MB", "Click for Actions"));
    private WheelImage ActionsClick => Shot("ka", new(OperationMode.Actions, PhotoFiles, WheelGeometry.HitHub, "2.4 MB", "Click for Convert"));

    private static int IndexOf(WheelImage w, string label) => Math.Max(0, w.Labels.ToList().IndexOf(label));

    /// <summary>Where the pointer should go to hover <paramref name="label"/> on wheel <paramref name="w"/>.</summary>
    private static Point SegmentPoint(WheelImage w, string label)
    {
        int i = IndexOf(w, label), n = Math.Max(1, w.Labels.Count);
        double a = WheelGeometry.MidAngle(i, n) * Math.PI / 180, r = WheelGeometry.LabelR * WheelSize / WheelGeometry.Extent * 1.22;
        // Out past the label and to its right: the pointer and the dragged file hang down-right, clear of the text.
        double dx = (0.012 * label.Length + 0.025) * WheelSize;
        return new Point(Center.X + r * Math.Cos(a) + dx, Center.Y + r * Math.Sin(a));
    }

    // ------------------------------------------------------------------ timeline helpers

    private static double Seg(double t, double a, double b) => Math.Clamp((t - a) / (b - a), 0, 1);
    private static double Ease(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    private static double Back(double x) { const double c1 = 1.4, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }
    private static Point Lerp(Point a, Point b, double k) => new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);
    private static Point Move(double t, double a, double b, Point from, Point to) => Lerp(from, to, Ease(Seg(t, a, b)));

    // ------------------------------------------------------------------ state setters

    private void Reset()
    {
        _content.Opacity = 1;
        SetCursor(Rest, false);
        Ghost(0, Rest);
        WheelState(null, 0, 1, 0);
        Tile(_tileB, 0);
        Tile(_tileC, 0);
        Select(false);
        ToastState(null, 0);
        Ripple(0, Center);
        Dwell(0, Center);
        Keys([]);
        foreach (var cap in _keyCaps.Values) Press(cap, false);
        _window.Visibility = _tileA.Visibility = _tileB.Visibility = _tileC.Visibility = Visibility.Visible;
        _settings.Visibility = Visibility.Collapsed;
        Caption(null);
    }

    private void SetCursor(Point p, bool pressed)
    {
        Place(_cursor, p.X - 1, p.Y - 1);
        _cursorScale.ScaleX = _cursorScale.ScaleY = pressed ? 0.9 : 1;
    }

    private void Ghost(double opacity, Point at)
    {
        _ghost.Opacity = opacity * 0.7;
        Place(_ghost, at.X + 6, at.Y + 14); // small, just below-right of the pointer
    }

    private void WheelState(WheelImage? image, double opacity, double scale, double spin)
    {
        _wheel.Source = image?.Image;
        _wheel.Opacity = opacity;
        _wheelScale.ScaleX = _wheelScale.ScaleY = scale;
        _wheelSpin.Angle = spin;
    }

    private static void Tile(Border tile, double k)
    {
        tile.Opacity = k;
        var s = (ScaleTransform)tile.RenderTransform;
        s.ScaleX = s.ScaleY = 0.6 + 0.4 * Back(k);
    }

    private void Select(bool on) => _tileA.Background = on ? (Brush)Application.Current.FindResource("Chango.AccentSubtle") : Brushes.Transparent;

    private void ToastState(BitmapSource? image, double k)
    {
        _toast.Source = image;
        _toast.Opacity = k;
        _toastSlide.Y = (1 - Ease(k)) * 16;
    }

    private void Ripple(double k, Point at)
    {
        double r = 8 + 22 * k;
        _ripple.Width = _ripple.Height = r * 2;
        _ripple.Opacity = k is > 0 and < 1 ? 1 - k : 0;
        Place(_ripple, at.X - r, at.Y - r);
    }

    private void Dwell(double k, Point at)
    {
        _dwell.Opacity = k is > 0 and < 1 ? 1 : 0;
        if (k <= 0 || k >= 1) return;
        const double r = 16;
        double a = k * 2 * Math.PI;
        var start = new Point(at.X, at.Y - r);
        var end = new Point(at.X + r * Math.Sin(a), at.Y - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, k > 0.5, SweepDirection.Clockwise, true));
        _dwell.Data = new PathGeometry([fig]);
        Place(_dwell, 0, 0);
    }

    private void Keys(string[] names)
    {
        foreach (var (name, cap) in _keyCaps) cap.Visibility = names.Contains(name) ? Visibility.Visible : Visibility.Collapsed;
        _keys.Opacity = names.Length > 0 ? 1 : 0;
        _keys.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Place(_keys, Center.X - _keys.DesiredSize.Width / 2, H - 46);
    }

    private void Press(Border cap, bool down)
    {
        cap.Background = (Brush)Application.Current.FindResource(down ? "Chango.Accent" : "Chango.Surface");
        cap.BorderBrush = (Brush)Application.Current.FindResource(down ? "Chango.AccentPressed" : "Chango.StrokeStrong");
        ((TextBlock)cap.Child).Foreground = (Brush)Application.Current.FindResource(down ? "Chango.OnAccent" : "Chango.TextPrimary");
        ((TranslateTransform)cap.RenderTransform).Y = down ? 2 : 0;
    }

    private void PressWindow(string key, double t, double from, double to) => Press(_keyCaps[key], t >= from && t < to);

    private void Caption(string? text)
    {
        if (!Captions) text = null;
        _caption.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        if (text is null) return;
        _captionText.Text = text;
        _caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Place(_caption, Center.X - _caption.DesiredSize.Width / 2, Center.Y - WheelSize / 2 - 4 - _caption.DesiredSize.Height);
    }

    private void FadeOut(double t, double from, double to) => _content.Opacity = 1 - Seg(t, from, to);

    // ------------------------------------------------------------------ scenes

    /// <summary>Poses the stage at time <paramref name="t"/> (seconds) of <paramref name="scene"/>.</summary>
    public void Apply(GuideScene scene, double t)
    {
        Reset();
        switch (scene)
        {
            case GuideScene.Hero:
                if (t < 3.2) DragScene(t, fade: false);
                else ReleaseScene(t - 3.2);
                break;
            case GuideScene.Drag: DragScene(t, fade: true); break;
            case GuideScene.Release: ReleaseScene(t); break;
            case GuideScene.Modes: ModesScene(t); break;
            case GuideScene.Groups: GroupsScene(t); break;
            case GuideScene.Keyboard: KeyboardScene(t); break;
            case GuideScene.Customize: CustomizeScene(t); break;
        }
    }

    /// <summary>Pick up the photo, carry it, tap the trigger key: the wheel opens around the pointer.</summary>
    private void DragScene(double t, bool fade)
    {
        Keys([_triggerKey]);
        var p = t < 1.3 ? Move(t, 0.2, 1.1, Rest, TileA) : Move(t, 1.4, 2.4, TileA, Center);
        bool pressed = t >= 1.25;
        SetCursor(p, pressed);
        _tileA.Opacity = pressed ? 0.55 : 1;
        Ghost(Seg(t, 1.3, 1.45), p);
        PressWindow(_triggerKey, t, 2.65, 2.85);
        double open = Seg(t, 2.75, 3.05);
        WheelState(ConvertIdle, open, 0.4 + 0.6 * Back(open), 0);
        if (fade) FadeOut(t, 4.9, 5.4);
    }

    /// <summary>Move onto JPG, let go: the wheel closes, photo.jpg appears next to the original, a notification confirms.</summary>
    private void ReleaseScene(double t)
    {
        var target = SegmentPoint(ConvertIdle, "JPG");
        var p = Move(t, 0.3, 1.1, Center, target);
        bool held = t < 1.6;
        SetCursor(p, held);
        _tileA.Opacity = held ? 0.55 : 1;
        double pop = Seg(t, 1.6, 1.8);
        WheelState(t >= 0.85 ? ConvertJpg : ConvertIdle, 1 - pop, 1 + 0.05 * pop, 0);
        Ghost(held ? 1 : 0, p);
        Tile(_tileB, Seg(t, 1.9, 2.25));
        ToastState(ToastImage("Saved photo.jpg", "In Pictures"), Seg(t, 2.1, 2.45));
        FadeOut(t, 5.7, 6.2);
    }

    /// <summary>Tapping the key again cycles Convert → Tools → Actions.</summary>
    private void ModesScene(double t)
    {
        Keys([_triggerKey]);
        SetCursor(Center, true);
        _tileA.Opacity = 0.55;
        Ghost(1, Center);
        double[] taps = [0.9, 2.1, 3.3];
        WheelImage[] wheels = [ConvertIdle, ToolsIdle, ActionsIdle, ConvertIdle];
        int stage = taps.Count(x => t >= x);
        double since = stage == 0 ? 1 : Seg(t, taps[stage - 1], taps[stage - 1] + 0.2);
        WheelState(wheels[stage], 0.4 + 0.6 * since, 1, -14 * (1 - Ease(since)));
        Press(_keyCaps[_triggerKey], taps.Any(x => t >= x && t < x + 0.2));
        FadeOut(t, 4.9, 5.4);
    }

    /// <summary>Rest on a group (Compress) to open its choices, then pick one.</summary>
    private void GroupsScene(double t)
    {
        var compress = SegmentPoint(ToolsIdle, "Compress");
        var oneMb = SegmentPoint(CompressGroup, "1 MB");
        var p = t < 2.0 ? Move(t, 0.3, 1.0, Center, compress) : Move(t, 2.1, 2.9, compress, oneMb);
        bool held = t < 3.3;
        SetCursor(p, held);
        _tileA.Opacity = held ? 0.55 : 1;
        Ghost(held ? 1 : 0, p);
        Dwell(Seg(t, 1.1, 1.7), compress);
        double pop = Seg(t, 3.3, 3.5);
        if (t < 1.7) WheelState(t >= 0.75 ? ToolsCompress : ToolsIdle, 1, 1, 0);
        else
        {
            double k = Seg(t, 1.7, 1.9);
            WheelState(t >= 2.6 ? Compress1Mb : CompressGroup, (0.4 + 0.6 * k) * (1 - pop), 1 + 0.05 * pop, -14 * (1 - Ease(k)));
        }
        _tileCName.Text = "photo (compressed).jpg";
        Tile(_tileC, Seg(t, 3.6, 3.95));
        ToastState(ToastImage("Saved photo (compressed).jpg", "In Pictures"), Seg(t, 3.8, 4.15));
        FadeOut(t, 6.7, 7.2);
    }

    /// <summary>No drag: select the photo, press Ctrl+Alt+Space, click an option.</summary>
    private void KeyboardScene(double t)
    {
        Keys(["Ctrl", "Alt", "Space"]);
        Select(true);
        PressWindow("Ctrl", t, 0.5, 1.4);
        PressWindow("Alt", t, 0.7, 1.4);
        PressWindow("Space", t, 0.9, 1.3);

        // Clicking the centre switches Convert → Tools → Actions → Convert, then pick JPG and click.
        double[] centreClicks = [2.2, 3.4, 4.6];
        const double pick = 6.4;
        var target = SegmentPoint(ConvertIdle, "JPG");
        var p = Move(t, 5.1, 5.9, Center, target);
        bool clicking = centreClicks.Any(c => t >= c && t < c + 0.15) || (t >= pick && t < pick + 0.15);
        SetCursor(p, clicking);
        double lastCentre = centreClicks.LastOrDefault(c => t >= c, -10);
        Ripple(t >= pick ? Seg(t, pick, pick + 0.5) : Seg(t, lastCentre, lastCentre + 0.5), t >= pick ? target : Center);

        int stage = centreClicks.Count(c => t >= c + 0.05);
        WheelImage image = stage switch
        {
            0 => ConvertClick,
            1 => ToolsClick,
            2 => ActionsClick,
            _ => t >= 5.6 ? ConvertJpg : ConvertIdle,
        };
        double open = Seg(t, 1.0, 1.3), pop = Seg(t, pick + 0.15, pick + 0.35);
        double since = stage == 0 ? 1 : Seg(t, centreClicks[stage - 1] + 0.05, centreClicks[stage - 1] + 0.25);
        WheelState(image, open * (0.4 + 0.6 * since) * (1 - pop), (0.4 + 0.6 * Back(open)) * (1 + 0.05 * pop), -14 * (1 - Ease(since)));
        Caption(t < 1.4 || t >= pick ? null : stage switch
        {
            0 => "Click the centre for Tools",
            1 => "Click again for Actions",
            2 => "And again to go back to Convert",
            _ => "Then click an option",
        });
        Tile(_tileB, Seg(t, pick + 0.4, pick + 0.75));
        ToastState(ToastImage("Saved photo.jpg", "In Pictures"), Seg(t, pick + 0.6, pick + 0.95));
        FadeOut(t, 10.1, 10.6);
    }

    /// <summary>
    /// Settings → Wheel: click BMP, TIFF and GIF off and the wheel shows only what's left; "Show everything" brings
    /// them back.
    /// </summary>
    private void CustomizeScene(double t)
    {
        _window.Visibility = _tileA.Visibility = _tileB.Visibility = _tileC.Visibility = Visibility.Collapsed;
        _settings.Visibility = Visibility.Visible;

        // The editor shows the same slices as the photo's Convert wheel: hide BMP and TIFF, drag PDF to the top, Save.
        var full = ConvertIdle;
        string Key(string label) => $"convert.image.{label.ToLowerInvariant()}";
        var entries = full.Labels.Select(l => new Settings.WheelEntry(new Settings.WheelChoice(Key(l), l, null), [])).ToList();
        (string Label, double At)[] clicks = [("BMP", 1.2), ("TIFF", 2.4)];
        const double press = 3.5, drop = 4.6, save = 5.8;
        int pdf = full.Labels.ToList().IndexOf("PDF");
        var savePoint = new Point(22 + GetLeft(_save) + _save.Width / 2, 24 + GetTop(_save) + _save.Height / 2);

        Point p = Move(t, 0.2, 1.0, Rest, EditorPoint(full, "BMP"));
        if (t >= 1.4) p = Move(t, 1.4, 2.2, EditorPoint(full, "BMP"), EditorPoint(full, "TIFF"));
        if (t >= 2.7) p = Move(t, 2.7, 3.3, EditorPoint(full, "TIFF"), EditorPoint(full, "PDF"));
        if (t >= press) p = Move(t, press + 0.1, drop - 0.1, EditorPoint(full, "PDF"), EditorPoint(full, full.Labels[0]));
        if (t >= drop + 0.2) p = Move(t, drop + 0.2, save - 0.2, EditorPoint(full, full.Labels[0]), savePoint);
        if (t >= save + 0.5) p = Move(t, save + 0.5, save + 1.2, savePoint, new Point(savePoint.X + 70, savePoint.Y + 26)); // off the button
        bool pressed = clicks.Any(c => t >= c.At && t < c.At + 0.15) || (t >= press && t < drop) || (t >= save && t < save + 0.15);
        SetCursor(p, pressed);

        int off = clicks.Count(c => t >= c.At + 0.1);
        string[] hidden = clicks.Take(off).Select(c => Key(c.Label)).ToArray();
        bool moved = t >= drop;
        if (moved) { var item = entries[pdf]; entries.RemoveAt(pdf); entries.Insert(0, item); }
        _editor.Show(entries, new HashSet<string>(hidden));
        if (t >= press + 0.2 && t < drop)
        {
            // Mid-drag: the slice follows the pointer round to the top and the others make room.
            var editorTopLeft = new Point(22 + GetLeft(_editor), 24 + GetTop(_editor));
            _editor.PreviewDrag(pdf, t >= press + 0.6 ? 0 : pdf, new Point(p.X - editorTopLeft.X, p.Y - editorTopLeft.Y));
        }
        else
        {
            _editor.EndPreview();
            _editor.SetHover(t < 2.7 ? full.Labels.ToList().IndexOf(t < 1.4 ? "BMP" : "TIFF") : t < press ? pdf : WheelGeometry.HitOutside);
        }
        _save.Opacity = t >= save ? 0.55 : 1;

        // The real wheel changes only once saved.
        bool saved = t >= save + 0.1;
        string[] order = moved ? entries.Select(e => e.Choice.Key).ToArray() : [];
        var wheel = saved ? Shot("cz", new(OperationMode.Convert, PhotoFiles, WheelGeometry.HitOutside, "2.4 MB", "photo.png", Hidden: hidden, Order: order)) : full;
        double since = saved ? Seg(t, save + 0.1, save + 0.35) : 1;
        double open = Seg(t, 0.1, 0.4);
        WheelState(wheel, open * (0.5 + 0.5 * since), (0.4 + 0.6 * Back(open)) * (0.96 + 0.04 * Ease(since)), -10 * (1 - Ease(since)));
        Caption(t < 0.9 ? "Settings → Wheel" : t < 2.7 ? "Click a slice to hide it" : t < drop + 0.2 ? "Drag a slice to move it" : !saved ? "Then press Save" : "Saved: your wheel, your way");
        FadeOut(t, 8.9, 9.4);
    }

    /// <summary>Stage position of a slice in the mini editor.</summary>
    private Point EditorPoint(WheelImage w, string label)
    {
        int i = IndexOf(w, label), n = Math.Max(1, w.Labels.Count);
        double a = WheelGeometry.MidAngle(i, n) * Math.PI / 180, r = WheelGeometry.LabelR * EditorSize / WheelGeometry.Disc;
        var c = new Point(22 + GetLeft(_editor) + EditorSize / 2, 24 + GetTop(_editor) + EditorSize / 2);
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    /// <summary>Renders one frame offscreen (for GIF export).</summary>
    public BitmapSource Render(GuideScene scene, double t, double scale)
    {
        Apply(scene, t);
        _cursor.Opacity = Pointer ? 1 : 0;
        if (!Pointer) _ghost.Opacity = 0;
        Measure(new Size(W, H));
        Arrange(new Rect(0, 0, W, H));
        UpdateLayout();
        var bmp = new RenderTargetBitmap((int)(W * scale), (int)(H * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(this);
        bmp.Freeze();
        return bmp;
    }
}
