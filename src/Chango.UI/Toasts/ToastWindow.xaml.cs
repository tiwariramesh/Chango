using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chango.Windows.Interop;

namespace Chango.UI.Toasts;

public enum ToastKind { Running, Success, Info, Warning, Error }

/// <summary>A floating progress/result card. Never takes focus; never appears in the taskbar.</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _autoClose = new();
    private bool _closing;
    private bool _clickAnywhere = true;

    public ToastWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Win32.AddExStyle(new WindowInteropHelper(this).Handle, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
        _autoClose.Tick += (_, _) => { _autoClose.Stop(); FadeOutAndClose(); };
        MouseEnter += (_, _) => _autoClose.Stop();
        MouseLeave += (_, _) => { if (_autoClose.Interval > TimeSpan.Zero && Kind != ToastKind.Running) _autoClose.Start(); };
        MouseLeftButtonUp += (_, e) => { if (_clickAnywhere && ShowButton.Visibility == Visibility.Visible && e.OriginalSource is not System.Windows.Controls.Button) ShowRequested?.Invoke(); };
        Shadow.Opacity = ThemeManager.Number("ToastShadowOpacity");
        SetIndeterminate(true);
    }

    public ToastKind Kind { get; private set; } = ToastKind.Running;

    public event Action? CancelRequested;
    public event Action? ShowRequested;
    public event Action? UndoRequested;

    /// <summary>Shows or hides the Undo link (only on a finished card).</summary>
    public void SetUndo(bool visible) => UndoButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>The content root, for offscreen design-review renders.</summary>
    public FrameworkElement ContentRoot => Root;

    public void SetRunning(string title, string detail, double fraction, bool indeterminate)
    {
        Kind = ToastKind.Running;
        TitleText.Text = title;
        DetailText.Text = detail;
        CloseButton.ToolTip = "Cancel";
        AutomationProperties(title, detail);
        SetIndeterminate(indeterminate);
        if (!indeterminate) SetArc(fraction);
        AnimateProgress(fraction);
    }

    /// <summary>Turns the "Show" link into a named action that only fires from the button itself, not a click on the card.</summary>
    public void SetAction(string label)
    {
        ShowButton.Content = label;
        ShowButton.ToolTip = null;
        _clickAnywhere = false;
    }

    public void SetResult(ToastKind kind, string title, string detail, bool canShow, TimeSpan autoClose)
    {
        Kind = kind;
        TitleText.Text = title;
        DetailText.Text = detail;
        CloseButton.ToolTip = "Dismiss";
        ShowButton.Visibility = canShow ? Visibility.Visible : Visibility.Collapsed;
        Cursor = canShow && _clickAnywhere ? Cursors.Hand : null;
        AutomationProperties(title, detail);

        RingSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        RingHost.Visibility = Visibility.Collapsed;
        Badge.Visibility = Visibility.Visible;
        (string glyph, string brush) = kind switch
        {
            ToastKind.Success => ("", "Chango.Success"),
            ToastKind.Warning => ("", "Chango.Warning"),
            ToastKind.Error => ("", "Chango.Danger"),
            _ => ("", "Chango.Accent"),
        };
        BadgeGlyph.Text = glyph;
        Badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, brush);
        ProgressFill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, brush);
        AnimateProgress(1);

        _autoClose.Stop();
        _autoClose.Interval = autoClose;
        if (autoClose > TimeSpan.Zero && !IsMouseOver) _autoClose.Start();
    }

    public void PlayEnter()
    {
        if (ThemeManager.Current.ReduceMotion) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    public void FadeOutAndClose()
    {
        if (_closing) return;
        _closing = true;
        _autoClose.Stop();
        if (ThemeManager.Current.ReduceMotion || !IsVisible) { Close(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (Kind == ToastKind.Running) CancelRequested?.Invoke();
        else FadeOutAndClose();
    }

    private void OnShow(object sender, RoutedEventArgs e) => ShowRequested?.Invoke();

    private void OnUndo(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();

    private void SetIndeterminate(bool on)
    {
        if (on)
        {
            SetArc(0.25);
            if (!ThemeManager.Current.ReduceMotion && RingSpin.HasAnimatedProperties == false)
                RingSpin.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            RingSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            RingSpin.Angle = 0;
        }
    }

    /// <summary>Draws a ring arc from 12 o'clock covering <paramref name="fraction"/> of the circle.</summary>
    private void SetArc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0.02, 0.999);
        const double r = 12.5, c = 14;
        double a = fraction * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        RingArc.Data = new PathGeometry([fig]);
    }

    private void AnimateProgress(double fraction)
    {
        double width = Math.Max(0, (ActualWidth > 0 ? ActualWidth : Width) - 32) * Math.Clamp(fraction, 0, 1);
        if (ThemeManager.Current.ReduceMotion)
        {
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = width;
        }
        else ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(160)));
    }

    private void AutomationProperties(string title, string detail) =>
        System.Windows.Automation.AutomationProperties.SetName(this, $"{title}. {detail}");
}
