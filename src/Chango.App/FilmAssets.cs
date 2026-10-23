using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chango.Core;
using Chango.Infrastructure;
using Chango.UI;
using Chango.UI.Onboarding;
using Chango.UI.Settings;
using Chango.UI.Toasts;
using Chango.UI.Tray;
using Chango.UI.Wheel;

namespace Chango.App;

/// <summary>
/// <c>Chango.exe --render-film &lt;folder&gt;</c>: renders the real UI states used by the launch film at 3× scale on
/// transparent backgrounds, so camera push-ins stay sharp. Same code paths as the live app; no screen capture.
/// </summary>
internal static class FilmAssets
{
    private const double Scale = 3.0;
    private static readonly double[] Scales = [Scale];

    public static int Run(string dir, OperationRegistry registry)
    {
        Directory.CreateDirectory(dir);
        File.Delete(Path.Combine(dir, "layout.txt"));
        int n = 0;
        string Tmp(string f) => Path.Combine(Path.GetTempPath(), f);
        var photo = new[] { "photo.jpg" };
        var batch = Enumerable.Range(1, 5).Select(i => $"photo-{i}.jpg").ToArray();
        var clip = new[] { "clip.mp4" };

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Current.Apply(theme, useWindowsAccent: false, reduceMotion: true);
            string t = theme == AppTheme.Dark ? "-dark" : "";
            var frost = FilmBackdrop(theme == AppTheme.Dark);

            // The lit slice is named, not numbered, so the film stays right when the wheel's layout changes.
            void Wheel(string name, OperationMode mode, string[] files, string? hi, string value, string detail)
            {
                var wheel = new RadialWheel();
                var items = OperationMenu.TopLevel(registry.Query(new FileSet(files.Select(Tmp)), mode));
                wheel.SetContent(mode, items, value, detail, animate: false);
                int index = hi is null ? -2 : items.ToList().FindIndex(i => i.Label == hi);
                if (hi is not null && index < 0) throw new InvalidOperationException($"No \"{hi}\" on the {mode} wheel for {name}.");
                wheel.SetHighlight(index, animate: false);
                if (hi is not null) File.AppendAllText(Path.Combine(dir, "layout.txt"), $"{name}{t}: {hi} = segment {index} of {items.Count}\n");
                Save(wheel.RenderPreview(frost, Scale, wallpaperBehind: false), Path.Combine(dir, $"wheel-{name}{t}.png"), ref n);
                wheel.Close();
            }

            Wheel("convert-idle", OperationMode.Convert, photo, null, "2.4 MB", "photo.jpg");
            Wheel("convert-png", OperationMode.Convert, photo, "PNG", "PNG", "Convert to PNG");
            Wheel("batch-idle", OperationMode.Convert, batch, null, "5 files", "JPG");
            Wheel("batch-png", OperationMode.Convert, batch, "PNG", "PNG", "Convert to PNG");
            Wheel("tools-idle", OperationMode.Tools, photo, null, "2.4 MB", "photo.jpg");
            Wheel("tools-rotate", OperationMode.Tools, photo, "Rotate & flip", "Rotate right", "photo.jpg");
            Wheel("actions-idle", OperationMode.Actions, photo, null, "2.4 MB", "photo.jpg");
            Wheel("actions-reveal", OperationMode.Actions, photo, "Show in folder", "Show in folder", "photo.jpg");
            Wheel("video-idle", OperationMode.Convert, clip, null, "18.4 MB", "clip.mp4");
            Wheel("video-mp3", OperationMode.Convert, clip, "MP3", "MP3", "Extract audio as MP3");

            void Toast(string name, Action<ToastWindow> setup)
            {
                var toast = new ToastWindow();
                setup(toast);
                RenderTransparent(toast.ContentRoot, 372, double.NaN, dir, $"toast-{name}{t}", ref n);
                toast.Close();
            }

            var none = TimeSpan.Zero;
            Toast("photo-saved", w => w.SetResult(ToastKind.Success, "Saved photo.png", "In Pictures", true, none));
            Toast("photo-copy-saved", w => w.SetResult(ToastKind.Success, "Saved photo (2).png", "In Pictures", true, none));
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                Toast($"batch-{k}", w => w.SetRunning("Converting to PNG", $"{k} of 5 · photo-{k + 1}.jpg", k / 5.0, indeterminate: k == 0));
            }
            Toast("batch-done", w => w.SetResult(ToastKind.Success, "5 files saved", "In Pictures", true, none));
            Toast("rotate-saved", w => w.SetResult(ToastKind.Success, "Saved photo (rotated).jpg", "In Pictures", true, none));
            foreach (var f in new[] { 0.25, 0.5, 0.75 })
                Toast($"video-{(int)(f * 100)}", w => w.SetRunning("Extracting audio as MP3", "clip.mp4", f, indeterminate: false));
            Toast("video-done", w => w.SetResult(ToastKind.Success, "Saved clip.mp3", "In Videos", true, none));

            var host = new FilmHost(registry, theme);
            var settings = new SettingsWindow(new SettingsViewModel(host));
            settings.ShowPage("Appearance");
            RenderElementOn(settings.ContentRoot, 880, 620, (Brush)Application.Current.FindResource("Chango.SurfaceBase"), dir, $"settings-appearance{t}", ref n);
            settings.Close();

            var tray = new TrayFlyout(new FilmTray());
            RenderTransparent(tray.ContentRoot, 300, double.NaN, dir, $"tray{t}", ref n);
            tray.Close();

            var guide = new OnboardingWindow("Shift");
            guide.ShowStep(1);
            RenderElementOn(guide.ContentRoot, 900, 600, (Brush)Application.Current.FindResource("Chango.SurfaceBase"), dir, $"guide-2{t}", ref n);
            guide.Close();
        }

        File.WriteAllText(Path.Combine(dir, "README.txt"), $"{n} film assets at {Scale}x · {DateTime.Now:yyyy-MM-dd HH:mm}\r\n");
        return 0;
    }

    private static void RenderTransparent(FrameworkElement e, double w, double h, string dir, string name, ref int n) =>
        RenderElementOn(e, w, h, null, dir, name, ref n);

    private static void RenderElementOn(FrameworkElement element, double width, double height, Brush? background, string dir, string name, ref int n)
    {
        element.Measure(new Size(width, double.IsNaN(height) ? double.PositiveInfinity : height));
        double h = double.IsNaN(height) ? element.DesiredSize.Height : height;
        element.Arrange(new Rect(0, 0, width, h));
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        if (background is not null)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, h));
            bmp.Render(dv);
        }
        bmp.Render(element);
        Save(bmp, Path.Combine(dir, name + ".png"), ref n);
    }

    private static void Save(BitmapSource bmp, string path, ref int n)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        n++;
    }

    /// <summary>What sits "behind" the wheel in the film: the off-white stage with soft neutral washes.</summary>
    private static ImageSource FilmBackdrop(bool dark)
    {
        double s = WheelGeometry.Extent;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // Apple-style neutral washes: pale mist blue and warm cream (no saturated colour).
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x12, 0x12, 0x14) : Color.FromRgb(0xFB, 0xFB, 0xFD)), null, new Rect(0, 0, s, s));
            dc.DrawEllipse(new RadialGradientBrush(dark ? Color.FromArgb(90, 0x50, 0x56, 0x62) : Color.FromArgb(150, 0xD9, 0xE2, 0xEE), Color.FromArgb(0, 0xD9, 0xE2, 0xEE)), null, new Point(s * 0.25, s * 0.3), s * 0.45, s * 0.45);
            dc.DrawEllipse(new RadialGradientBrush(dark ? Color.FromArgb(70, 0x5A, 0x55, 0x50) : Color.FromArgb(140, 0xF1, 0xE9, 0xDF), Color.FromArgb(0, 0xF1, 0xE9, 0xDF)), null, new Point(s * 0.8, s * 0.75), s * 0.4, s * 0.4);
        }
        var bmp = new RenderTargetBitmap((int)s, (int)s, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private sealed class FilmHost(OperationRegistry registry, AppTheme theme) : ISettingsHost
    {
        public AppSettings Settings { get; } = new() { Theme = theme };
        public void Update(Action<AppSettings> change) => change(Settings);
        public bool LaunchAtStartup { get; set; } = true;
        public bool AccentFallbackActive => false;
        public IEnumerable<(string Category, string Detail)> Capabilities => registry.Providers.SelectMany(p => p.DescribeCapabilities());
        public string VersionText => "Version 0.1.0-alpha";
        public string SettingsPath => "";
        public string LogsPath => "";
        public string? LicensePath => null;
        public void PreviewWheel() { }
        public void ShowOnboarding() { }
        public void OpenPath(string path) { }
        public void ResetSettings() { }
    }

    private sealed class FilmTray : ITrayCommands
    {
        public bool Paused { get; set; }
        public string TriggerKeyName => "Shift";
        public void OpenSettings(string? page = null) { }
        public void ShowOnboarding() { }
        public void Quit() { }
    }
}
