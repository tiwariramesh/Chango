using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Skottie;

namespace Chango.UI;

/// <summary>
/// Plays a Lottie animation (Chango's logo intro) with Skia's Skottie player: each frame is rendered into a
/// <see cref="WriteableBitmap"/> in step with WPF's render loop. Shows the finished frame immediately (so offscreen
/// renders and reduced motion get the complete logo), plays once when loaded, and falls back to a still image if the
/// animation can't be read.
/// </summary>
public sealed class LottieView : Image
{
    /// <summary>pack:// URI of the Lottie JSON.</summary>
    public string? AnimationSource { get; set; }

    /// <summary>pack:// URI of a still image shown if the animation can't be loaded.</summary>
    public string? FallbackSource { get; set; }

    public bool PlayOnLoad { get; set; } = true;

    /// <summary>Raised when a play finishes (straight away under reduced motion).</summary>
    public event Action? Completed;

    private Animation? _animation;
    private bool _failed;
    private WriteableBitmap? _bitmap;
    private readonly Stopwatch _clock = new();
    private bool _playing;

    public LottieView()
    {
        Stretch = Stretch.Uniform;
        Loaded += (_, _) => { if (PlayOnLoad) Play(); };
        Unloaded += (_, _) => StopRendering();
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        ShowFrame(1); // the complete logo until it plays
    }

    public void Play()
    {
        if (!EnsureLoaded()) { Completed?.Invoke(); return; }
        if (ThemeManager.Current.ReduceMotion) { ShowFrame(1); Completed?.Invoke(); return; }
        ShowFrame(0);
        _clock.Restart();
        if (!_playing)
        {
            _playing = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double progress = _clock.Elapsed.TotalSeconds / Math.Max(0.01, _animation!.Duration.TotalSeconds);
        ShowFrame(Math.Min(progress, 1));
        if (progress < 1) return;
        StopRendering();
        Completed?.Invoke();
    }

    private void StopRendering()
    {
        if (!_playing) return;
        _playing = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    /// <summary>Draws the animation at <paramref name="progress"/> (0 = start, 1 = end).</summary>
    public void ShowFrame(double progress)
    {
        if (!EnsureLoaded()) return;
        int px = PixelSize();
        if (_bitmap is null || _bitmap.PixelWidth != px)
        {
            _bitmap = new WriteableBitmap(px, px, 96, 96, PixelFormats.Pbgra32, null);
            Source = _bitmap;
        }
        _animation!.Seek(progress, null);
        _bitmap.Lock();
        try
        {
            var info = new SKImageInfo(px, px, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _bitmap.BackBuffer, _bitmap.BackBufferStride);
            surface.Canvas.Clear(SKColors.Transparent);
            _animation.Render(surface.Canvas, new SKRect(0, 0, px, px));
            surface.Canvas.Flush();
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, px, px));
        }
        finally { _bitmap.Unlock(); }
    }

    /// <summary>Square side in device pixels: the larger of the set width and height, at the monitor's scale.</summary>
    private int PixelSize()
    {
        double side = Math.Max(double.IsNaN(Width) ? ActualWidth : Width, double.IsNaN(Height) ? ActualHeight : Height);
        if (side <= 0 || double.IsNaN(side)) side = 128;
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.5; // crisp up to 150 % before it's on screen
        return Math.Clamp((int)Math.Ceiling(side * scale), 16, 2048);
    }

    private bool EnsureLoaded()
    {
        if (_animation is not null) return true;
        if (_failed) return false;
        try
        {
            if (AnimationSource is { } uri && Application.GetResourceStream(new Uri(uri)) is { } resource)
            {
                using var stream = resource.Stream;
                if (Animation.TryCreate(stream, out var animation) && animation is not null)
                {
                    _animation = animation;
                    return true;
                }
            }
        }
        catch (Exception) { /* fall through to the still image */ }
        _failed = true;
        if (FallbackSource is { } still) Source = new BitmapImage(new Uri(still));
        return false;
    }
}
