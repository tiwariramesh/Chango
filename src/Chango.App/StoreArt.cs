using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Chango.Core;
using Chango.Infrastructure;
using Chango.UI;
using Chango.UI.Onboarding;

namespace Chango.App;

/// <summary>
/// <c>Chango.exe --render-store &lt;folder&gt;</c>: Microsoft Store artwork at 4K. Screenshots pair a short headline
/// with a real moment from the app (the guide's scenes, re-rendered at high resolution, and the real windows);
/// plus the square box art, the 2:3 poster and the 16:9 hero image. Offscreen, no screen capture.
/// </summary>
internal static class StoreArt
{
    private const double W = 1920, H = 1080, Scale = 2.0; // 3840 × 2160 output

    private sealed record Shot(string Name, string Headline, string Subline, GuideScene Scene, double At, bool Pointer = true);

    private static readonly Shot[] Shots =
    [
        new("01-convert", "Change files into any format.", "Drag a file, tap Shift, let go on PDF, Word, JPG, MP3 and more.", GuideScene.Release, 1.3),
        new("02-result", "The new file lands right beside the old one.", "Chango never changes your original and never uploads anything.", GuideScene.Release, 2.9),
        new("03-tools", "More choices, one + away.", "Pause on a slice marked + and its options fan out.", GuideScene.Groups, 2.75),
        new("04-modes", "Three wheels: Convert, Tools, Actions.", "Each tap of Shift moves to the next wheel.", GuideScene.Modes, 2.6, Pointer: false),
        new("05-keyboard", "Rather click? Press Ctrl + Alt + Space.", "Select files, press the shortcut and pick a format.", GuideScene.Keyboard, 3.05, Pointer: false),
        new("06-customize", "Your wheel, your way.", "Hide slices you never use and put your favourites first.", GuideScene.Customize, 7.6),
    ];

    public static int Run(string dir, OperationRegistry registry)
    {
        Directory.CreateDirectory(dir);
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Current.Apply(theme, useWindowsAccent: false, reduceMotion: true);
            string suffix = theme == AppTheme.Dark ? "-dark" : "";
            var assets = new GuideAssets(registry, scale: 5);
            var stage = new GuideStage(assets, "Shift") { Captions = false };

            foreach (var shot in Shots)
            {
                stage.Pointer = shot.Pointer;
                var scene = stage.Render(shot.Scene, shot.At, Scale * 2.6);
                Save(Compose(shot.Headline, shot.Subline, scene, 1560), Path.Combine(dir, $"screenshot-{shot.Name}{suffix}.png"));
            }

            // The photo editor, as a real window.
            var photo = GuideStageExtras.SamplePhoto(1600);
            var editor = new Chango.UI.Editor.PhotoEditorWindow(photo, "Sunset.jpg");
            double k = photo.PixelWidth / 356.0;
            editor.ReviewState("Markup",
            [
                new Chango.UI.Editor.ArrowMark(Color.FromRgb(0xFF, 0x3B, 0x30), 4 * k, new Point(64 * k, 56 * k), new Point(146 * k, 118 * k)),
                new Chango.UI.Editor.BoxMark(Color.FromRgb(0xFF, 0xCC, 0x00), 4 * k, new Rect(164 * k, 128 * k, 136 * k, 134 * k)), // round the sun
                new Chango.UI.Editor.TextMark(Colors.White, 0, new Point(36 * k, 276 * k), "Golden hour", 30 * k),
            ]);
            var window = RenderWindow((FrameworkElement)editor.Content, 1120, 780);
            editor.Close();
            Save(Compose("Quick edits, built in.", "Crop, adjust, mark up and redact before you share.", window, 1240), Path.Combine(dir, $"screenshot-07-editor{suffix}.png"));
            assets.Dispose();
        }

        ThemeManager.Current.Apply(AppTheme.Light, useWindowsAccent: false, reduceMotion: true);
        Logos(dir, registry);
        return 0;
    }

    // ------------------------------------------------------------------ composition

    /// <summary>Headline and subline over a soft backdrop, with the app moment below on a floating card.</summary>
    private static BitmapSource Compose(string headline, string subline, BitmapSource moment, double width)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            Background(dc, new Rect(0, 0, W, H));
            var ink = new SolidColorBrush(ThemeManager.Token("TextPrimary"));
            var soft = new SolidColorBrush(ThemeManager.Token("TextSecondary"));
            var title = Text(headline, 60, FontWeights.SemiBold, ink, W - 240);
            var sub = Text(subline, 26, FontWeights.Normal, soft, W - 360);
            dc.DrawText(title, new Point((W - title.Width) / 2, 92));
            dc.DrawText(sub, new Point((W - sub.Width) / 2, 92 + title.Height + 16));

            double h = width * moment.PixelHeight / moment.PixelWidth;
            double top = 92 + title.Height + 16 + sub.Height + 56;
            double maxH = H - top - 84; // breathing room below the card
            if (h > maxH) { width *= maxH / h; h = maxH; }
            var card = new Rect((W - width) / 2, top, width, h);
            Shadow(dc, card, 18);
            dc.PushClip(new RectangleGeometry(card, 18, 18));
            dc.DrawImage(moment, card);
            dc.Pop();
        }
        return Render(v, W, H);
    }

    /// <summary>A quiet monochrome backdrop with a soft light falloff (no colour: the brand is black and white).</summary>
    private static void Background(DrawingContext dc, Rect r)
    {
        Color a = ThemeManager.Token("SurfaceBase"), b = ThemeManager.Token("AccentSubtle");
        dc.DrawRectangle(new LinearGradientBrush(a, b, new Point(0, 0), new Point(0.3, 1)), null, r);
        var glow = new RadialGradientBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), Colors.Transparent) { Center = new Point(0.5, 0.15), GradientOrigin = new Point(0.5, 0.15), RadiusX = 0.6, RadiusY = 0.5 };
        dc.DrawRectangle(glow, null, r);
    }

    /// <summary>A soft, layered drop shadow under a rounded card.</summary>
    private static void Shadow(DrawingContext dc, Rect card, double radius)
    {
        for (int i = 6; i >= 1; i--)
        {
            var spread = card;
            spread.Inflate(i * 4, i * 4);
            spread.Offset(0, i * 3);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(10 - i), 0, 0, 0)), null, spread, radius + i * 4, radius + i * 4);
        }
    }

    private static FormattedText Text(string s, double size, FontWeight weight, Brush brush, double max) =>
        new(s, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, 1.0)
        { MaxTextWidth = max, TextAlignment = TextAlignment.Left };

    private static BitmapSource RenderWindow(FrameworkElement content, double w, double h)
    {
        content.Measure(new Size(w, h));
        content.Arrange(new Rect(0, 0, w, h));
        content.UpdateLayout();
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.FindResource("Chango.SurfaceBase"), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        return Render(v, w, h, 3);
    }

    // ------------------------------------------------------------------ logos

    /// <summary>Box art (1:1), poster (2:3) and hero (16:9): the icon and wordmark on the same quiet backdrop.</summary>
    private static void Logos(string dir, OperationRegistry registry)
    {
        var icon = new BitmapImage(new Uri("pack://application:,,,/Chango.UI;component/Assets/chango-1024.png"));
        void Art(string name, double w, double h, bool wordmark, double iconSize, double y)
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                Background(dc, new Rect(0, 0, w, h));
                var r = new Rect((w - iconSize) / 2, y, iconSize, iconSize);
                dc.DrawImage(icon, r); // the mark has no tile, so no shadow under it
                if (wordmark)
                {
                    var ink = new SolidColorBrush(ThemeManager.Token("TextPrimary"));
                    var title = Text("Chango", iconSize * 0.36, FontWeights.SemiBold, ink, w);
                    var sub = Text(Brand.Subtitle, iconSize * 0.13, FontWeights.Normal, new SolidColorBrush(ThemeManager.Token("TextSecondary")), w);
                    dc.DrawText(title, new Point((w - title.Width) / 2, r.Bottom + iconSize * 0.18));
                    dc.DrawText(sub, new Point((w - sub.Width) / 2, r.Bottom + iconSize * 0.18 + title.Height + iconSize * 0.04));
                }
            }
            Save(Render(v, w, h, 1), Path.Combine(dir, name));
        }
        Art("boxart-1x1-2160.png", 2160, 2160, wordmark: false, 1080, 540);
        Art("poster-2x3-1440x2160.png", 1440, 2160, wordmark: true, 680, 560);
        Art("storelogo-300.png", 300, 300, wordmark: false, 300, 0);

        // Hero art (16:9): no text (the Store overlays the title); the wheel at work over a quiet backdrop.
        var assets = new GuideAssets(registry, scale: 6);
        var stage = new GuideStage(assets, "Shift");
        var moment = stage.Render(GuideScene.Release, 1.3, 6);
        var hv = new DrawingVisual();
        using (var dc = hv.RenderOpen())
        {
            Background(dc, new Rect(0, 0, W, H));
            double cw = W * 0.62, ch = cw * GuideStage.H / GuideStage.W;
            var card = new Rect((W - cw) / 2, (H - ch) / 2, cw, ch);
            Shadow(dc, card, 22);
            dc.PushClip(new RectangleGeometry(card, 22, 22));
            dc.DrawImage(moment, card);
            dc.Pop();
        }
        Save(Render(hv, W, H), Path.Combine(dir, "hero-16x9-3840x2160.png"));
        assets.Dispose();
    }

    private static BitmapSource Render(Visual v, double w, double h, double scale = Scale)
    {
        var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(v);
        bmp.Freeze();
        return bmp;
    }

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

/// <summary>Small helpers shared with the guide's drawing.</summary>
internal static class GuideStageExtras
{
    public static BitmapSource SamplePhoto(int size) => GuideStage.Photo(size);
}
