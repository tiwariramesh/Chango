using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitProbe;

public enum WheelMode { Convert, Tools, Actions }

public sealed record WheelItem(string Id, string Label, string Verb, string Insert);

/// <summary>
/// The probe's tiny operation set. Convert and Tools are genuine image operations through WIC
/// (built into Windows: no third-party dependency). Actions are non-destructive.
/// </summary>
public static class Operations
{
    private static readonly Dictionary<string, string> ImageFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "PNG", [".jpg"] = "JPG", [".jpeg"] = "JPG", [".jfif"] = "JPG",
        [".bmp"] = "BMP", [".tif"] = "TIFF", [".tiff"] = "TIFF", [".gif"] = "GIF",
    };

    private static readonly (string Label, string Ext)[] Targets =
        [("PNG", ".png"), ("JPG", ".jpg"), ("BMP", ".bmp"), ("TIFF", ".tiff"), ("GIF", ".gif")];

    public static bool IsImage(string path) => ImageFormats.ContainsKey(Path.GetExtension(path));

    public static string FormatOf(string path) => ImageFormats.GetValueOrDefault(Path.GetExtension(path), "?");

    /// <summary>Only options valid for every file are offered (brief §3.7).</summary>
    public static List<WheelItem> ItemsFor(WheelMode mode, IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        bool allImages = files.All(IsImage);

        switch (mode)
        {
            case WheelMode.Convert when allImages:
                // Hide a target only when *every* file is already in that format.
                var formats = files.Select(FormatOf).Distinct().ToList();
                return Targets.Where(t => !(formats.Count == 1 && formats[0] == t.Label))
                              .Select(t => new WheelItem($"convert{t.Ext}", t.Label, "Convert to %1", t.Label))
                              .ToList();
            case WheelMode.Tools when allImages:
                return
                [
                    new("tool.rotate-cw", "Rotate ↻", "%1", "Rotate right"),
                    new("tool.rotate-ccw", "Rotate ↺", "%1", "Rotate left"),
                    new("tool.grayscale", "Grayscale", "%1", "Grayscale"),
                ];
            case WheelMode.Actions:
                return
                [
                    new("action.copy-path", "Copy path", "%1", "Copy path"),
                    new("action.copy-name", "Copy name", "%1", "Copy name"),
                    new("action.reveal", "Reveal", "%1", "Show in folder"),
                ];
            default:
                return [];
        }
    }

    /// <summary>Executes on the calling (UI) thread for actions; image work runs on a dedicated STA thread.</summary>
    public static async Task<List<(string Source, string? Output, string? Error)>> ExecuteAsync(WheelItem item, IReadOnlyList<string> files)
    {
        if (item.Id.StartsWith("action.", StringComparison.Ordinal))
            return RunAction(item, files);

        return await RunSta(() =>
        {
            var results = new List<(string, string?, string?)>();
            foreach (var file in files)
            {
                try { results.Add((file, RunImageOperation(item, file), null)); }
                catch (Exception ex) { results.Add((file, null, ex.Message)); }
            }
            return results;
        });
    }

    private static string? RunImageOperation(WheelItem item, string file)
    {
        BitmapSource src = Load(file);
        string? newExt = null, suffix = null;
        BitmapSource output;

        if (item.Id.StartsWith("convert.", StringComparison.Ordinal))
        {
            newExt = item.Id["convert".Length..];
            if (FormatOf(file) == item.Label) throw new InvalidOperationException($"already {item.Label}, skipped");
            output = src;
        }
        else
        {
            (output, suffix) = item.Id switch
            {
                "tool.rotate-cw" => ((BitmapSource)new TransformedBitmap(src, new RotateTransform(90)), "rotated"),
                "tool.rotate-ccw" => (new TransformedBitmap(src, new RotateTransform(-90)), "rotated"),
                "tool.grayscale" => (new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0), "grayscale"),
                _ => throw new NotSupportedException(item.Id),
            };
        }

        string ext = newExt ?? Path.GetExtension(file);
        int w = output.PixelWidth, h = output.PixelHeight;
        return SafeOutput.WriteBeside(file, newExt, suffix,
            write: stream => Encode(output, ext, stream),
            validate: temp =>
            {
                var check = Load(temp);
                return check.PixelWidth == w && check.PixelHeight == h ? null
                    : $"decoded {check.PixelWidth}x{check.PixelHeight}, expected {w}x{h}";
            });
    }

    public static BitmapSource Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        // Normalise to BGRA32: drops embedded metadata/colour contexts that not every encoder accepts.
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();
        return bgra;
    }

    private static void Encode(BitmapSource image, string ext, Stream stream)
    {
        BitmapEncoder encoder = ext.ToLowerInvariant() switch
        {
            ".png" => new PngBitmapEncoder(),
            ".jpg" or ".jpeg" or ".jfif" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            _ => throw new NotSupportedException(ext),
        };

        // Formats without alpha: flatten onto white instead of letting transparent pixels turn black.
        bool noAlpha = encoder is JpegBitmapEncoder or BmpBitmapEncoder;
        encoder.Frames.Add(BitmapFrame.Create(noAlpha ? FlattenOnWhite(image) : image));
        encoder.Save(stream);
    }

    private static BitmapSource FlattenOnWhite(BitmapSource src)
    {
        if (src.Format == PixelFormats.Gray8) return src;
        var bgra = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        var rgb = new byte[w * 3 * h];
        for (int i = 0, j = 0; i < px.Length; i += 4, j += 3)
        {
            int a = px[i + 3];
            rgb[j] = (byte)((px[i] * a + 255 * (255 - a)) / 255);
            rgb[j + 1] = (byte)((px[i + 1] * a + 255 * (255 - a)) / 255);
            rgb[j + 2] = (byte)((px[i + 2] * a + 255 * (255 - a)) / 255);
        }
        return BitmapSource.Create(w, h, bgra.DpiX, bgra.DpiY, PixelFormats.Bgr24, null, rgb, w * 3);
    }

    private static List<(string, string?, string?)> RunAction(WheelItem item, IReadOnlyList<string> files)
    {
        switch (item.Id)
        {
            case "action.copy-path":
                Clipboard.SetText(string.Join(Environment.NewLine, files.Select(f => $"\"{f}\"")));
                break;
            case "action.copy-name":
                Clipboard.SetText(string.Join(Environment.NewLine, files.Select(Path.GetFileName)));
                break;
            case "action.reveal":
                // Paths come from CF_HDROP; Windows file names cannot contain quotes.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{files[0]}\"") { UseShellExecute = false });
                break;
        }
        return files.Select(f => (f, (string?)null, (string?)null)).ToList();
    }

    public static Task<T> RunSta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "OrbitProbe image worker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
