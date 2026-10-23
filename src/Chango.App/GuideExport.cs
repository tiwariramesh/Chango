using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chango.Conversion.Media;
using Chango.Core;
using Chango.Infrastructure;
using Chango.UI;
using Chango.UI.Onboarding;

namespace Chango.App;

/// <summary>
/// <c>Chango.exe --render-guide &lt;folder&gt;</c>: exports the guide's demo scenes as looping GIFs (light and dark)
/// for the README and docs. Same scenes, same real wheel renders as the in-app guide; offscreen, no screen capture.
/// </summary>
internal static class GuideExport
{
    private const int Fps = 10;
    private const double Scale = 1.0;

    public static int Run(string dir, OperationRegistry registry)
    {
        Directory.CreateDirectory(dir);
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Current.Apply(theme, useWindowsAccent: false, reduceMotion: true);
            var assets = new GuideAssets(registry);
            var stage = new GuideStage(assets, "Shift", flatBackground: true);
            foreach (var scene in Enum.GetValues<GuideScene>())
            {
                int frames = (int)Math.Round(GuideStage.DurationOf(scene) * Fps);
                var encoder = new GifBitmapEncoder();
                var offsets = new List<(int X, int Y)>();
                byte[]? previous = null;
                for (int i = 0; i < frames; i++)
                {
                    BitmapSource rgb = new FormatConvertedBitmap(stage.Render(scene, i / (double)Fps, Scale), PixelFormats.Bgr32, null, 0);
                    int w = rgb.PixelWidth, h = rgb.PixelHeight;
                    var pixels = new byte[w * h * 4];
                    rgb.CopyPixels(pixels, w * 4, 0);
                    // Only the part that changed since the last frame is stored; the rest stays on screen.
                    var box = previous is null ? new Int32Rect(0, 0, w, h) : Changed(previous, pixels, w, h);
                    previous = pixels;
                    BitmapSource part = box.Width == w && box.Height == h ? rgb : new CroppedBitmap(rgb, box);
                    encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(part, PixelFormats.Indexed8, new BitmapPalette(part, 256), 0)));
                    offsets.Add((box.X, box.Y));
                }
                using var ms = new MemoryStream();
                encoder.Save(ms);
                string name = $"guide-{scene.ToString().ToLowerInvariant()}{(theme == AppTheme.Dark ? "-dark" : "")}.gif";
                File.WriteAllBytes(Path.Combine(dir, name), GifLoop.AddTimingAndLoop(ms.ToArray(), 100 / Fps, offsets));
            }
            assets.Dispose();
        }
        return 0;
    }

    /// <summary>
    /// <c>Chango.exe --render-guide-frames &lt;folder&gt;</c>: the same scenes as PNG frames at 2× and 30 fps
    /// (&lt;folder&gt;/&lt;scene&gt;[-dark]/0000.png …), for encoding into sharp looping web videos (website/).
    /// </summary>
    public static int Frames(string dir, OperationRegistry registry, int fps = 30, double scale = 2.0)
    {
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Current.Apply(theme, useWindowsAccent: false, reduceMotion: true);
            var assets = new GuideAssets(registry, scale: scale * 2);
            var stage = new GuideStage(assets, "Shift");
            foreach (var scene in Enum.GetValues<GuideScene>())
            {
                string sub = Path.Combine(dir, $"{scene.ToString().ToLowerInvariant()}{(theme == AppTheme.Dark ? "-dark" : "")}");
                Directory.CreateDirectory(sub);
                int frames = (int)Math.Round(GuideStage.DurationOf(scene) * fps);
                for (int i = 0; i < frames; i++)
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(stage.Render(scene, i / (double)fps, scale)));
                    using var fs = File.Create(Path.Combine(sub, $"{i:D4}.png"));
                    enc.Save(fs);
                }
            }
            assets.Dispose();
        }
        return 0;
    }

    /// <summary>Bounding box of the pixels that differ (at least 1×1, so every frame keeps its timing).</summary>
    private static Int32Rect Changed(byte[] a, byte[] b, int w, int h)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int i = row + x * 4;
                if (a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2]) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                y1 = y;
            }
        }
        return x1 < 0 ? new Int32Rect(0, 0, 1, 1) : new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }
}
