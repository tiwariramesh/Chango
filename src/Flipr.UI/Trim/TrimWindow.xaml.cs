using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Flipr.Core;

namespace Flipr.UI.Trim;

/// <summary>
/// Choose the part of a video or audio file to keep. Plays the selection; Save copy returns the range, and the
/// caller writes "name (trimmed).ext". <see cref="Result"/> completes with null if the window is closed.
/// </summary>
public partial class TrimWindow : Window
{
    private readonly TimeSpan _length;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly TaskCompletionSource<TrimRange?> _result = new();
    private bool _playing;

    public TrimWindow(string path, TimeSpan length, bool isVideo)
    {
        InitializeComponent();
        _length = length > TimeSpan.Zero ? length : TimeSpan.FromSeconds(1);
        Title = $"{Path.GetFileName(path)} · Trim";
        FileNameText.Text = Path.GetFileName(path);
        AudioPlaceholder.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
        Player.Source = new Uri(path);
        Player.MediaOpened += (_, _) => { Player.Pause(); Show(0); };
        Player.MediaEnded += (_, _) => Pause();
        Player.Play(); // opening needs Play; MediaOpened pauses straight away on the first frame
        Bar.RangeChanged += f => { Pause(); Show(f); UpdateText(); };
        Bar.Seek += f => { Show(f); };
        _tick.Tick += (_, _) => OnTick();
        PreviewKeyDown += OnKey;
        Closed += (_, _) => { _tick.Stop(); Player.Close(); _result.TrySetResult(null); };
        UpdateText();
    }

    public Task<TrimRange?> Result => _result.Task;

    /// <summary>Design review (--render-ui): shows a range without user input.</summary>
    public void ReviewRange(double start, double end) { Bar.SetRange(start, end); UpdateText(); }

    private TimeSpan At(double fraction) => TimeSpan.FromTicks((long)(_length.Ticks * fraction));

    private void Show(double fraction)
    {
        Player.Position = At(fraction);
        Bar.SetPlayhead(fraction);
    }

    private void UpdateText()
    {
        RangeText.Text = $"{Format(At(Bar.Start))} – {Format(At(Bar.End))} of {Format(_length)}";
        LengthText.Text = Format(At(Bar.End) - At(Bar.Start));
        SaveButton.IsEnabled = Bar.Start > 0.0005 || Bar.End < 0.9995;
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.f") : t.ToString(@"m\:ss\.f");

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_playing) { Pause(); return; }
        if (Bar.Playhead >= Bar.End - 0.002 || Bar.Playhead < Bar.Start) Show(Bar.Start);
        Player.Play();
        _playing = true;
        PlayGlyph.Text = "";
        _tick.Start();
    }

    private void Pause()
    {
        Player.Pause();
        _playing = false;
        PlayGlyph.Text = "";
        _tick.Stop();
    }

    private void OnTick()
    {
        double f = Player.Position.Ticks / (double)_length.Ticks;
        if (f >= Bar.End) { Pause(); Show(Bar.End); return; }
        Bar.SetPlayhead(f);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { OnPlay(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _result.TrySetResult(new TrimRange(At(Bar.Start), At(Bar.End)));
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
