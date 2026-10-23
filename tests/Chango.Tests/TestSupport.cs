global using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chango.Conversion;
using Chango.Core;

namespace Chango.Tests;

/// <summary>Temporary folder with real image fixtures, deleted after the test.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ChangoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Writes a gradient image; <paramref name="hole"/> punches a transparent circle in the middle.</summary>
    public string Image(string name, int width, int height, bool hole = false)
    {
        string path = File(name);
        StaWorker.RunAsync(() =>
        {
            int stride = width * 4;
            var px = new byte[stride * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                px[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                px[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                px[i + 2] = 180;
                double dx = x - width / 2.0, dy = y - height / 2.0;
                px[i + 3] = hole && dx * dx + dy * dy < height * height / 16.0 ? (byte)0 : (byte)255;
            }
            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, px, stride);
            BitmapEncoder enc = System.IO.Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".png" => new PngBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                ".tif" or ".tiff" => new TiffBitmapEncoder(),
                _ => new JpegBitmapEncoder(),
            };
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(path, FileMode.CreateNew);
            enc.Save(fs);
        }).GetAwaiter().GetResult();
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Records clipboard/reveal calls instead of touching the real shell.</summary>
public sealed class FakeShell : IShellIntegration
{
    public string? Clipboard { get; private set; }
    public List<string> Revealed { get; } = [];
    public void SetClipboardText(string text) => Clipboard = text;
    public void Reveal(IReadOnlyList<string> paths) => Revealed.AddRange(paths);
}

/// <summary>Collects provider outcomes synchronously.</summary>
public sealed class Collector : IProgress<FileOutcome>
{
    public List<FileOutcome> Outcomes { get; } = [];
    public void Report(FileOutcome value) { lock (Outcomes) Outcomes.Add(value); }
}

public static class Ctx
{
    public static OperationContext Default(FakeShell? shell = null, OutputOptions? output = null) =>
        new() { Output = output ?? new OutputOptions(), Shell = shell ?? new FakeShell(), JpegQuality = 90 };
}
