using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using OrbitDrop.Windows.Interop;

namespace OrbitDrop.UI.Onboarding;

/// <summary>
/// The guide: animated demonstrations of each part of the gesture (built from the real wheel and notification),
/// then a hands-on practice step with a real photo that the user drags, converts and sees appear.
/// </summary>
public partial class OnboardingWindow : Window
{
    private sealed record Step(GuideScene? Scene, string Label, string Heading, string Body);

    private readonly Step[] _steps;
    private readonly GuideStage? _stage;
    private readonly string _triggerKey;
    private readonly DispatcherTimer _frame = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly string _practiceFolder;
    private int _step;
    private Point _pressAt;
    private string? _practiceResult;

    /// <param name="assets">Real wheel and notification renders; without them (design review) the stage stays empty.</param>
    public OnboardingWindow(string triggerKey, IGuideAssets? assets = null, string? practiceFolder = null)
    {
        InitializeComponent();
        _triggerKey = triggerKey;
        _practiceFolder = practiceFolder ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrbitDrop guide");
        _steps =
        [
            new(GuideScene.Hero, "WELCOME", "Do it right where your files are",
                $"Convert, compress, edit and share files without opening an app. Drag a file, tap {triggerKey}, let go on what you want. That's the whole idea."),
            new(GuideScene.Drag, "STEP 1", $"Drag a file, then tap {triggerKey}",
                $"Start dragging any file in File Explorer, on the desktop or in VS Code, and keep holding it. Tap {triggerKey} once (no need to hold it): the wheel opens around your pointer."),
            new(GuideScene.Release, "STEP 2", "Let go on what you want",
                "Move onto an option and release the mouse button. The result appears next to the original, which is never changed. Let go on the centre, or press Esc, to cancel."),
            new(GuideScene.Modes, "STEP 3", $"Tap {triggerKey} again for more",
                $"Each tap switches the wheel: Convert (PNG, JPG, PDF, MP3…), Tools (edit, compress, resize, trim…) and Actions (copy to, share, zip, float…)."),
            new(GuideScene.Groups, "TIP", "Options with dots open into more choices",
                "Rest on one for a moment and its choices appear; rest on the centre to go back. In a hurry? Let go on it straight away for the most common choice."),
            new(GuideScene.Keyboard, "TIP", "Prefer the keyboard? Click instead",
                "Select files in File Explorer, on the desktop or in VS Code, press Ctrl + Alt + Space, then click an option. For Tools and Actions, click the centre of the wheel (or tap Shift, or press the shortcut again). Esc closes it."),
            new(GuideScene.Customize, "TIP", "Make the wheel yours",
                "Open Settings → Wheel and you'll see your wheel. Click a slice to hide it (click again to bring it back), drag a slice to move it, then press Save. Reset brings back the original wheel."),
            new(null, "PRACTICE", "Try it now",
                $"Drag the photo on the left a little way, tap {triggerKey} while you're still holding it, and let go on JPG. It's a sample file, so nothing of yours is touched."),
        ];
        Step2Title.Text = $"Tap {triggerKey}";
        foreach (var _ in _steps) Dots.Children.Add(new Ellipse { Height = 6, Margin = new Thickness(3, 0, 3, 0) });

        if (assets is not null)
        {
            _stage = new GuideStage(assets, triggerKey);
            StageHost.Content = _stage;
        }
        PracticeThumb.ImageSource = GuideStage.Photo(400);
        _frame.Tick += (_, _) => Tick();
        // Size to the content once, then stay put: the window must not jump between steps.
        Loaded += (_, _) => { SizeToContent = SizeToContent.Manual; ShowStep(_step); };
        Closed += (_, _) => _frame.Stop();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Right) { OnNext(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.Left) { OnBack(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        ShowStep(0);
    }

    /// <summary>Raised when the user finishes or skips the guide.</summary>
    public event Action? Completed;

    /// <summary>The content root, for offscreen design-review renders.</summary>
    public FrameworkElement ContentRoot => Root;

    public int StepCount => _steps.Length;

    public void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, _steps.Length - 1);
        var s = _steps[_step];
        StepLabel.Text = s.Label;
        Heading.Text = s.Heading;
        Body.Text = s.Body;
        for (int i = 0; i < Dots.Children.Count; i++)
        {
            var dot = (Ellipse)Dots.Children[i];
            dot.SetResourceReference(Shape.FillProperty, i == _step ? "Orbit.Accent" : "Orbit.StrokeStrong");
            dot.Width = i == _step ? 18 : 6;
        }
        bool last = _step == _steps.Length - 1;
        BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        SkipButton.Visibility = last ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = last ? (_practiceResult is null ? "Finish" : "Done") : _step == 0 ? "Show me" : "Next";

        StageHost.Visibility = s.Scene is null ? Visibility.Collapsed : Visibility.Visible;
        Practice.Visibility = s.Scene is null ? Visibility.Visible : Visibility.Collapsed;
        if (s.Scene is null) PreparePractice();

        _clock.Restart();
        if (_stage is not null && s.Scene is { } scene)
        {
            if (ThemeManager.Current.ReduceMotion) { _frame.Stop(); _stage.Apply(scene, KeyFrame(scene)); }
            else { Tick(); _frame.Start(); }
        }
        else _frame.Stop();

        if (!ThemeManager.Current.ReduceMotion && IsLoaded)
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(160)));
    }

    /// <summary>Design review (--render-ui): a step with its scene frozen on a representative frame.</summary>
    public void ShowStepStill(int step)
    {
        ShowStep(step);
        _frame.Stop();
        PracticeBob.BeginAnimation(TranslateTransform.YProperty, null);
        if (_stage is not null && _steps[_step].Scene is { } scene) _stage.Apply(scene, KeyFrame(scene));
    }

    /// <summary>With reduced motion, one representative still per scene.</summary>
    private static double KeyFrame(GuideScene scene) => scene switch
    {
        GuideScene.Hero => 4.6, GuideScene.Release => 2.6, GuideScene.Drag => 3.4, GuideScene.Modes => 2.5,
        GuideScene.Groups => 2.7, GuideScene.Customize => 4.3, _ => 3.0,
    };

    private void Tick()
    {
        if (_stage is null || _steps[_step].Scene is not { } scene) return;
        double t = _clock.Elapsed.TotalSeconds % GuideStage.DurationOf(scene);
        _stage.Apply(scene, t);
    }

    // ------------------------------------------------------------------ practice

    private string PracticeFile => System.IO.Path.Combine(_practiceFolder, "Try me.png");

    /// <summary>A fresh sample photo in a folder of its own, and a reset checklist.</summary>
    private void PreparePractice()
    {
        try
        {
            if (Directory.Exists(_practiceFolder))
                foreach (var f in Directory.EnumerateFileSystemEntries(_practiceFolder))
                {
                    try { if (File.Exists(f)) File.Delete(f); else Directory.Delete(f, recursive: true); } catch { /* in use: leave it */ }
                }
            Directory.CreateDirectory(_practiceFolder);
            if (!File.Exists(PracticeFile))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(GuideStage.Photo(800)));
                using var fs = File.Create(PracticeFile);
                enc.Save(fs);
            }
        }
        catch { /* the practice still works if the file already exists */ }
        _practiceResult = null;
        SetCheck(1, false); SetCheck(2, false); SetCheck(3, false);
        Check3Hint.Text = "A JPG copy appears next to the photo.";
        ShowResultButton.Visibility = Visibility.Collapsed;
        if (!ThemeManager.Current.ReduceMotion)
            PracticeBob.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    private void SetCheck(int n, bool done)
    {
        var (badge, text) = n switch { 1 => (Check1, Check1Text), 2 => (Check2, Check2Text), _ => (Check3, Check3Text) };
        badge.SetResourceReference(Border.BackgroundProperty, done ? "Orbit.Accent" : "Orbit.Surface");
        badge.SetResourceReference(Border.BorderBrushProperty, done ? "Orbit.Accent" : "Orbit.StrokeStrong");
        text.SetResourceReference(TextBlock.ForegroundProperty, done ? "Orbit.OnAccent" : "Orbit.TextSecondary");
        text.FontFamily = done ? (FontFamily)FindResource("Orbit.Font.Icons") : FontFamily;
        text.Text = done ? "" : n.ToString();
    }

    private void OnPracticeDown(object sender, MouseButtonEventArgs e) => _pressAt = e.GetPosition(this);

    private void OnPracticeMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !File.Exists(PracticeFile)) return;
        var d = e.GetPosition(this) - _pressAt;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        SetCheck(1, true);
        // Watch for the trigger key while the drag runs (OLE's drag loop keeps the dispatcher pumping).
        int vk = _triggerKey == "Ctrl" ? 0x11 : 0x10;
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        watch.Tick += (_, _) => { if (Win32.IsDown(vk)) SetCheck(2, true); };
        watch.Start();
        InternalDrag.Starting?.Invoke();
        try { DragDrop.DoDragDrop(PracticeTile, new DataObject(DataFormats.FileDrop, new[] { PracticeFile }), DragDropEffects.Copy | DragDropEffects.Link); }
        finally { watch.Stop(); }
    }

    /// <summary>Called by the app whenever a job finishes; reacts when it was the practice photo.</summary>
    public void OnJobFinished(IReadOnlyList<string> sources, IReadOnlyList<string> outputs)
    {
        if (!sources.Any(s => string.Equals(s, PracticeFile, StringComparison.OrdinalIgnoreCase)) || outputs.Count == 0) return;
        _practiceResult = outputs[0];
        SetCheck(1, true); SetCheck(2, true); SetCheck(3, true);
        Check3Hint.Text = $"Nice! You made \"{System.IO.Path.GetFileName(_practiceResult)}\". That's all there is to it.";
        ShowResultButton.Visibility = Visibility.Visible;
        if (_step == _steps.Length - 1) NextButton.Content = "Done";
    }

    private void OnShowResult(object sender, RoutedEventArgs e)
    {
        if (_practiceResult is not null) ShowResultRequested?.Invoke(_practiceResult);
    }

    /// <summary>The app opens File Explorer with the file selected.</summary>
    public event Action<string>? ShowResultRequested;

    // ------------------------------------------------------------------ navigation

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step < _steps.Length - 1) ShowStep(_step + 1);
        else Finish();
    }

    private void OnBack(object sender, RoutedEventArgs e) { if (_step > 0) ShowStep(_step - 1); }

    private void OnSkip(object sender, RoutedEventArgs e) => Finish();

    private void Finish()
    {
        Completed?.Invoke();
        Close();
    }
}
