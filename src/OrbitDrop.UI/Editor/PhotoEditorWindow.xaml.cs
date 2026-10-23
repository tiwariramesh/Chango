using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitDrop.UI.Editor;

/// <summary>
/// The photo editor: crop, adjust, markup and redact. Edits stay as a list of operations in image coordinates until
/// Save, which renders the full-resolution result once (<see cref="EditRenderer.Export"/>). The original is never
/// written; <see cref="Result"/> completes with the edited bitmap, or null if the window is closed without saving.
/// </summary>
public partial class PhotoEditorWindow : Window
{
    private const double MaxPreviewSide = 1800;

    private readonly BitmapSource _source;
    private readonly int _w, _h;
    private readonly double _previewScale;
    private readonly byte[] _previewBase;
    private readonly int _pw, _ph;
    private readonly List<Mark> _marks = [];
    private readonly TaskCompletionSource<BitmapSource?> _result = new();

    private Adjustments _adjust = new();
    private Rect _crop;
    private double _aspect; // 0 = free

    private enum Drag { None, Draw, CropNew, CropMove, N, S, E, W, NE, NW, SE, SW }
    private Drag _drag;
    private Point _start;
    private Rect _cropAtStart;
    private List<Point>? _penPoints;
    private TextBox? _textBox;
    private Point _textAt;

    public PhotoEditorWindow(BitmapSource source, string fileName)
    {
        InitializeComponent();
        _source = source;
        _w = source.PixelWidth;
        _h = source.PixelHeight;
        Title = $"{fileName} · Edit";
        FileNameText.Text = fileName;

        _previewScale = Math.Min(1, MaxPreviewSide / Math.Max(_w, _h));
        BitmapSource preview = _previewScale < 1
            ? new TransformedBitmap(source, new ScaleTransform(_previewScale, _previewScale))
            : source;
        var bgra = new FormatConvertedBitmap(preview, PixelFormats.Bgra32, null, 0);
        _pw = bgra.PixelWidth;
        _ph = bgra.PixelHeight;
        _previewBase = EditRenderer.Pixels(bgra);

        Doc.Width = Layer.Width = Photo.Width = _w;
        Doc.Height = Layer.Height = Photo.Height = _h;
        Layer.ImageSize = new Size(_w, _h);
        Layer.Marks = _marks;
        _crop = new Rect(0, 0, _w, _h);
        Layer.Crop = _crop;

        RefreshPreview();
        ModeMarkup.IsChecked = true;
        Stage.SizeChanged += (_, _) => UpdateUnit();
        PreviewKeyDown += OnKey;
        Closed += (_, _) => _result.TrySetResult(null);
        UpdateCropText();
        UpdateUndo();
    }

    public Task<BitmapSource?> Result => _result.Task;

    /// <summary>Design review (--render-ui): sets a mode and some marks without user input.</summary>
    public void ReviewState(string mode, IEnumerable<Mark> marks, Rect? crop = null)
    {
        _marks.AddRange(marks);
        if (crop is { } c) { _crop = c; Layer.Crop = c; UpdateCropText(); }
        (mode switch { "Crop" => ModeCrop, "Adjust" => ModeAdjust, "Redact" => ModeRedact, _ => ModeMarkup }).IsChecked = true;
        RefreshPreview();
        UpdateUndo();
        Layer.Refresh();
    }

    // ------------------------------------------------------------------ modes

    private enum Mode { Crop, Adjust, Markup, Redact }
    private Mode Current => ModeCrop.IsChecked == true ? Mode.Crop : ModeAdjust.IsChecked == true ? Mode.Adjust
        : ModeRedact.IsChecked == true ? Mode.Redact : Mode.Markup;

    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (CropPanel is null) return; // during InitializeComponent
        CommitText();
        var m = Current;
        CropPanel.Visibility = m == Mode.Crop ? Visibility.Visible : Visibility.Collapsed;
        AdjustPanel.Visibility = m == Mode.Adjust ? Visibility.Visible : Visibility.Collapsed;
        MarkupPanel.Visibility = m == Mode.Markup ? Visibility.Visible : Visibility.Collapsed;
        RedactPanel.Visibility = m == Mode.Redact ? Visibility.Visible : Visibility.Collapsed;
        Layer.ShowCrop = m == Mode.Crop;
        Doc.Cursor = m == Mode.Adjust ? Cursors.Arrow : Cursors.Cross;
        Layer.Refresh();
    }

    /// <summary>How many image pixels make one screen pixel at the current zoom (for handle sizes and hit areas).</summary>
    private void UpdateUnit()
    {
        double shown = Math.Min(Stage.ActualWidth / _w, Stage.ActualHeight / _h);
        Layer.Unit = shown > 0 ? 1 / shown : 1;
        Layer.Refresh();
    }

    // ------------------------------------------------------------------ preview

    private void RefreshPreview()
    {
        var px = (byte[])_previewBase.Clone();
        EditRenderer.Adjust(px, _adjust);
        foreach (var r in _marks.OfType<RedactMark>().Where(m => m.Kind == RedactKind.Pixelate))
            EditRenderer.Pixelate(px, _pw, _ph, r.Area, (double)_pw / _w, Math.Max(_w, _h));
        Photo.Source = EditRenderer.FromPixels(px, _pw, _ph);
    }

    private void OnAdjust(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightnessSlider is null || WarmthSlider is null) return;
        _adjust = new Adjustments(BrightnessSlider.Value, ContrastSlider.Value, SaturationSlider.Value, WarmthSlider.Value);
        RefreshPreview();
    }

    private void OnResetSlider(object sender, MouseButtonEventArgs e) => ((Slider)sender).Value = 0;

    private void OnResetAdjust(object sender, RoutedEventArgs e)
    {
        BrightnessSlider.Value = ContrastSlider.Value = SaturationSlider.Value = WarmthSlider.Value = 0;
    }

    // ------------------------------------------------------------------ markup and redaction

    private Color CurrentColor =>
        Swatches.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Background is SolidColorBrush b ? b.Color : Colors.Red;

    private double SizeFactor => SizeS.IsChecked == true ? 0.5 : SizeL.IsChecked == true ? 2 : 1;
    private double StrokeWidth => Math.Max(2, Math.Max(_w, _h) / 320.0) * SizeFactor;
    private double TextSize => Math.Max(12, Math.Max(_w, _h) / 30.0) * SizeFactor;

    private void OnDocDown(object sender, MouseButtonEventArgs e)
    {
        var p = Clamp(e.GetPosition(Doc));
        _start = p;
        switch (Current)
        {
            case Mode.Crop:
                _drag = HitCrop(p);
                _cropAtStart = _crop;
                break;
            case Mode.Markup when ToolText.IsChecked == true:
                CommitText();
                BeginText(p);
                return;
            case Mode.Markup or Mode.Redact:
                CommitText();
                _drag = Drag.Draw;
                _penPoints = ToolPen.IsChecked == true && Current == Mode.Markup ? [p] : null;
                break;
            default:
                return;
        }
        Doc.CaptureMouse();
        e.Handled = true;
    }

    private void OnDocMove(object sender, MouseEventArgs e)
    {
        var p = Clamp(e.GetPosition(Doc));
        if (_drag == Drag.None)
        {
            if (Current == Mode.Crop) Doc.Cursor = CursorFor(HitCrop(p));
            return;
        }
        if (_drag == Drag.Draw)
        {
            Layer.Pending = MarkFor(_start, p);
            Layer.Refresh();
        }
        else
        {
            _crop = DragCrop(p);
            Layer.Crop = _crop;
            UpdateCropText();
            Layer.Refresh();
        }
    }

    private void OnDocUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == Drag.None) return;
        Doc.ReleaseMouseCapture();
        var p = Clamp(e.GetPosition(Doc));
        if (_drag == Drag.Draw && MarkFor(_start, p) is { } mark && IsMeaningful(mark))
        {
            _marks.Add(mark);
            if (mark is RedactMark { Kind: RedactKind.Pixelate }) RefreshPreview();
        }
        _drag = Drag.None;
        _penPoints = null;
        Layer.Pending = null;
        Layer.Refresh();
        UpdateUndo();
    }

    private Mark? MarkFor(Point a, Point b)
    {
        var area = new Rect(a, b);
        if (Current == Mode.Redact) return new RedactMark(area, RedactPixelate.IsChecked == true ? RedactKind.Pixelate : RedactKind.Black);
        if (ToolPen.IsChecked == true)
        {
            if (_penPoints is null) return null;
            if (_penPoints.Count == 0 || (_penPoints[^1] - b).Length > StrokeWidth / 3) _penPoints.Add(b);
            return new PenMark(CurrentColor, StrokeWidth, _penPoints.ToList());
        }
        if (ToolArrow.IsChecked == true) return new ArrowMark(CurrentColor, StrokeWidth, a, b);
        if (ToolBox.IsChecked == true) return new BoxMark(CurrentColor, StrokeWidth, area);
        return null;
    }

    private bool IsMeaningful(Mark m) => m switch
    {
        PenMark => true,
        ArrowMark a => (a.To - a.From).Length > StrokeWidth * 2,
        BoxMark b => b.Area.Width > 3 && b.Area.Height > 3,
        RedactMark r => r.Area.Width > 3 && r.Area.Height > 3,
        _ => true,
    };

    // ------------------------------------------------------------------ text

    private void BeginText(Point at)
    {
        _textAt = at;
        _textBox = new TextBox
        {
            FontSize = TextSize, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = new SolidColorBrush(CurrentColor), Background = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(0), Padding = new Thickness(0), MinWidth = TextSize * 3, MinHeight = 0,
            AcceptsReturn = false, CaretBrush = new SolidColorBrush(CurrentColor),
        };
        _textBox.Template = PlainTextBoxTemplate();
        Canvas.SetLeft(_textBox, at.X);
        Canvas.SetTop(_textBox, at.Y);
        _textBox.KeyDown += (_, k) =>
        {
            if (k.Key == Key.Enter) { CommitText(); k.Handled = true; }
            else if (k.Key == Key.Escape) { CancelText(); k.Handled = true; }
        };
        _textBox.LostKeyboardFocus += (_, _) => CommitText();
        TextHost.Children.Add(_textBox);
        _textBox.Focus();
    }

    private static ControlTemplate PlainTextBoxTemplate()
    {
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.AppendChild(host);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    private void CommitText()
    {
        if (_textBox is not { } box) return;
        _textBox = null;
        TextHost.Children.Remove(box);
        if (!string.IsNullOrWhiteSpace(box.Text))
        {
            // TextBox content has a small built-in inset; match it so the saved text lands where it was typed.
            _marks.Add(new TextMark(((SolidColorBrush)box.Foreground).Color, 0, _textAt + new Vector(2, 0), box.Text.Trim(), box.FontSize));
            Layer.Refresh();
            UpdateUndo();
        }
    }

    private void CancelText()
    {
        if (_textBox is not { } box) return;
        _textBox = null;
        TextHost.Children.Remove(box);
    }

    // ------------------------------------------------------------------ crop

    private double Grip => Math.Max(_w, _h) / 40.0;

    private Drag HitCrop(Point p)
    {
        var c = _crop;
        double g = Math.Max(Grip, 14 * Layer.Unit);
        bool l = Math.Abs(p.X - c.Left) < g, r = Math.Abs(p.X - c.Right) < g, t = Math.Abs(p.Y - c.Top) < g, b = Math.Abs(p.Y - c.Bottom) < g;
        bool inX = p.X > c.Left - g && p.X < c.Right + g, inY = p.Y > c.Top - g && p.Y < c.Bottom + g;
        if (t && l) return Drag.NW;
        if (t && r) return Drag.NE;
        if (b && l) return Drag.SW;
        if (b && r) return Drag.SE;
        if (t && inX) return _aspect != 0 ? Drag.NE : Drag.N;
        if (b && inX) return _aspect != 0 ? Drag.SE : Drag.S;
        if (l && inY) return _aspect != 0 ? Drag.SW : Drag.W;
        if (r && inY) return _aspect != 0 ? Drag.SE : Drag.E;
        if (c.Contains(p)) return Drag.CropMove;
        return Drag.CropNew;
    }

    private static Cursor CursorFor(Drag d) => d switch
    {
        Drag.N or Drag.S => Cursors.SizeNS,
        Drag.E or Drag.W => Cursors.SizeWE,
        Drag.NE or Drag.SW => Cursors.SizeNESW,
        Drag.NW or Drag.SE => Cursors.SizeNWSE,
        Drag.CropMove => Cursors.SizeAll,
        _ => Cursors.Cross,
    };

    private Rect DragCrop(Point p)
    {
        var c = _cropAtStart;
        var d = p - _start;
        const double min = 16;
        Rect r;
        switch (_drag)
        {
            case Drag.CropMove:
                r = new Rect(Math.Clamp(c.X + d.X, 0, _w - c.Width), Math.Clamp(c.Y + d.Y, 0, _h - c.Height), c.Width, c.Height);
                return r;
            case Drag.CropNew:
                return Constrain(_start, p);
            case Drag.N: r = new Rect(c.Left, Math.Min(c.Bottom - min, c.Top + d.Y), c.Width, 0); r.Height = c.Bottom - r.Top; return ClipToImage(r);
            case Drag.S: return ClipToImage(new Rect(c.Left, c.Top, c.Width, Math.Max(min, c.Height + d.Y)));
            case Drag.W: r = new Rect(Math.Min(c.Right - min, c.Left + d.X), c.Top, 0, c.Height); r.Width = c.Right - r.Left; return ClipToImage(r);
            case Drag.E: return ClipToImage(new Rect(c.Left, c.Top, Math.Max(min, c.Width + d.X), c.Height));
            default:
            {
                // Corners: the opposite corner stays put.
                var anchor = _drag switch { Drag.NW => c.BottomRight, Drag.NE => c.BottomLeft, Drag.SW => c.TopRight, _ => c.TopLeft };
                return Constrain(anchor, p);
            }
        }
    }

    /// <summary>A rectangle from <paramref name="anchor"/> towards <paramref name="p"/>, keeping the aspect ratio if one is set and staying inside the image.</summary>
    private Rect Constrain(Point anchor, Point p)
    {
        double dx = p.X - anchor.X, dy = p.Y - anchor.Y;
        double w = Math.Max(16, Math.Abs(dx)), h = Math.Max(16, Math.Abs(dy));
        if (_aspect > 0)
        {
            if (w / h > _aspect) w = h * _aspect; else h = w / _aspect;
        }
        double sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
        double maxW = sx > 0 ? _w - anchor.X : anchor.X, maxH = sy > 0 ? _h - anchor.Y : anchor.Y;
        double k = Math.Min(1, Math.Min(maxW / w, maxH / h));
        w *= k; h *= k;
        return new Rect(sx > 0 ? anchor.X : anchor.X - w, sy > 0 ? anchor.Y : anchor.Y - h, w, h);
    }

    private Rect ClipToImage(Rect r)
    {
        r.Intersect(new Rect(0, 0, _w, _h));
        return r.IsEmpty ? _crop : r;
    }

    private void OnAspect(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        double value = double.Parse(tag, CultureInfo.InvariantCulture);
        _aspect = value < 0 ? (double)_w / _h : value;
        if (_aspect > 0 && Layer is not null)
        {
            // The largest centred rectangle with that shape.
            double w = _w, h = w / _aspect;
            if (h > _h) { h = _h; w = h * _aspect; }
            _crop = new Rect((_w - w) / 2, (_h - h) / 2, w, h);
            Layer.Crop = _crop;
            UpdateCropText();
            Layer.Refresh();
        }
    }

    private void OnResetCrop(object sender, RoutedEventArgs e)
    {
        _crop = new Rect(0, 0, _w, _h);
        Layer.Crop = _crop;
        UpdateCropText();
        Layer.Refresh();
    }

    private void UpdateCropText()
    {
        if (CropSizeText is null) return;
        var c = CropRect();
        CropSizeText.Text = $"{c.Width} × {c.Height} px";
    }

    private Int32Rect CropRect()
    {
        int x = (int)Math.Round(Math.Clamp(_crop.X, 0, _w - 1)), y = (int)Math.Round(Math.Clamp(_crop.Y, 0, _h - 1));
        int w = (int)Math.Round(Math.Clamp(_crop.Width, 1, _w - x)), h = (int)Math.Round(Math.Clamp(_crop.Height, 1, _h - y));
        return new Int32Rect(x, y, w, h);
    }

    // ------------------------------------------------------------------ commands

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _w), Math.Clamp(p.Y, 0, _h));

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void Undo()
    {
        if (_textBox is not null) { CancelText(); return; }
        if (_marks.Count == 0) return;
        var last = _marks[^1];
        _marks.RemoveAt(_marks.Count - 1);
        if (last is RedactMark { Kind: RedactKind.Pixelate }) RefreshPreview();
        Layer.Refresh();
        UpdateUndo();
    }

    private void UpdateUndo() => UndoButton.IsEnabled = _marks.Count > 0;

    private bool HasChanges => _marks.Count > 0 || !_adjust.IsIdentity || CropRect() != new Int32Rect(0, 0, _w, _h);

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_textBox is not null && e.Key != Key.Escape) return; // typing
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Undo(); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save(); e.Handled = true; }
        else if (e.Key == Key.Escape && _textBox is null) { Close(); e.Handled = true; }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        CommitText();
        if (!HasChanges) { Close(); return; }
        Cursor = Cursors.Wait;
        try
        {
            var result = EditRenderer.Export(_source, _adjust, _marks, CropRect());
            _result.TrySetResult(result);
        }
        finally { Cursor = null; }
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
