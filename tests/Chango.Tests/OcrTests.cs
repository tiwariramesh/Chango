using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chango.Conversion;
using Chango.Tools;
using Windows.Media.Ocr;

namespace Chango.Tests;

public class OcrTests
{
    [Fact]
    public async Task Reads_text_from_an_image()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return; // no OCR language on this machine (e.g. a bare CI image)
        using var t = new TempFolder();
        string path = t.File("note.png");
        await StaWorker.RunAsync(() =>
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 900, 200));
                dc.DrawText(new FormattedText("Hello Chango 2026", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 64, Brushes.Black, 1.0), new Point(40, 50));
            }
            var bmp = new RenderTargetBitmap(900, 200, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
        });
        string text = await ImageTextProvider.RecognizeAsync(engine, path);
        Assert.Contains("Chango", text);
        Assert.Contains("2026", text);
    }
}
