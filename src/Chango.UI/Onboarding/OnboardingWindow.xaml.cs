using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chango.Windows.Interop;

namespace Chango.UI.Onboarding;

/// <summary>
/// The Chango tour: a teal brand panel listing the chapters, and beside it animated demonstrations of each part of the
/// gesture (built from the real wheel and notification), ending in a hands-on practice with a real photo that the
/// user drags, changes to JPG and sees appear.
/// </summary>
public partial class OnboardingWindow : Window
{
    private sealed record Step(GuideScene? Scene, string Chapter, string Label, string Heading, string Body);

    private static readonly Brush PanelInk = Frozen(new SolidColorBrush(Color.FromRgb(0x01, 0x1F, 0x1E)));
    private static readonly Brush PanelPill = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

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
        _practiceFolder = practiceFolder ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Chango guide");
        _steps =
        [
            new(GuideScene.Hero, "Welcome", "WELCOME TO CHANGO", "G'day! Meet Chango",
                "Chango changes your files into the format you need, right here on your PC. No uploads, no sign-ups, no limits and no fees, ever. It waits quietly in the notification area and springs to life when you drag a file."),
            new(GuideScene.Drag, "Drag", "STEP 1 · DRAG", $"Grab a file, then tap {triggerKey}",
                $"Start dragging from File Explorer, the desktop or VS Code and keep the mouse button down. A quick tap of {triggerKey} is all it takes: the Chango wheel pops open right under your pointer."),
            new(GuideScene.Release, "Change", "STEP 2 · CHANGE", "Drop it on the format you want",
                "Slide onto PDF, Word, JPG, MP3 or anything else and let go. The new file appears next to the old one in a moment, and your original stays exactly as it was. Changed your mind? Let go in the middle, or press Esc."),
            new(GuideScene.Modes, "Three wheels", "STEP 3 · SWITCH", $"Tap {triggerKey} again for more wheels",
                $"Convert changes the format. Tools edit, shrink, resize, trim and merge. Actions copy, share, archive or park files on a shelf for later. Every tap of {triggerKey} moves to the next wheel."),
            new(GuideScene.Groups, "Sub-wheels", "TIP", "A + means there's more inside",
                "Rest on a slice marked + and its choices fan out around it; rest in the middle to step back. Let go straight away and you get the most popular choice."),
            new(GuideScene.Keyboard, "Keyboard", "TIP", "Prefer clicking? Press Ctrl + Alt + Space",
                "Select files in File Explorer, on the desktop or in VS Code, press Ctrl + Alt + Space and click what you want. Click the middle of the wheel (or tap Shift) to switch wheels; Esc closes it."),
            new(GuideScene.Customize, "Your wheel", "TIP", "Shape the wheel around you",
                "In Settings → Wheel, click a slice to hide it (click again to bring it back) and drag one to a new spot, then Save. Reset puts everything back the way it was."),
            new(null, "Try it", "PRACTICE", "Your turn: give it a go",
                $"Drag the practice photo a little way, tap {triggerKey} while you're still holding it, and drop it on JPG. It's a sample file, so nothing of yours changes."),
        ];
        Step2Title.Text = $"Tap {triggerKey}";
        foreach (var step in _steps) Dots.Children.Add(ChapterRow(step.Chapter));

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
        StepCounter.Text = $"{_step + 1} of {_steps.Length}";
        for (int i = 0; i < Dots.Children.Count; i++) PaintChapter((Border)Dots.Children[i], i);
        bool last = _step == _steps.Length - 1;
        BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        SkipButton.Visibility = last ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = last ? (_practiceResult is null ? "Finish" : "Done") : _step == 0 ? "Let's go" : "Next";

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

    /// <summary>A chapter in the brand panel: a marker (number, or a tick once done) and its name. Clickable.</summary>
    private Border ChapterRow(string chapter)
    {
        int index = Dots.Children.Count;
        var marker = new TextBlock { Width = 22, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = chapter, FontSize = 14, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel();
        row.Children.Add(marker);
        row.Children.Add(label);
        var pill = new Border { Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(8, 0, 12, 0), Margin = new Thickness(-8, 1, 0, 1), Child = row, Cursor = Cursors.Hand };
        pill.MouseLeftButtonUp += (_, _) => ShowStep(index);
        return pill;
    }

    private void PaintChapter(Border pill, int index)
    {
        var row = (DockPanel)pill.Child;
        var marker = (TextBlock)row.Children[0];
        var label = (TextBlock)row.Children[1];
        bool current = index == _step, done = index < _step;
        pill.Background = current ? PanelPill : Brushes.Transparent;
        marker.Foreground = label.Foreground = PanelInk;
        label.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
        pill.Opacity = current || done ? 1 : 0.7;
        marker.FontFamily = done ? (FontFamily)FindResource("Chango.Font.Icons") : (FontFamily)FindResource("Chango.Font.Text");
        marker.Text = done ? "" : (index + 1).ToString(); // a tick for chapters already seen
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
        badge.SetResourceReference(Border.BackgroundProperty, done ? "Chango.Accent" : "Chango.Surface");
        badge.SetResourceReference(Border.BorderBrushProperty, done ? "Chango.Accent" : "Chango.StrokeStrong");
        text.SetResourceReference(TextBlock.ForegroundProperty, done ? "Chango.OnAccent" : "Chango.TextSecondary");
        text.FontFamily = done ? (FontFamily)FindResource("Chango.Font.Icons") : FontFamily;
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
        Check3Hint.Text = $"Beauty! You made \"{System.IO.Path.GetFileName(_practiceResult)}\". That's all there is to it.";
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
