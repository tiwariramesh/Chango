using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Core;
using OrbitDrop.Infrastructure;

namespace OrbitDrop.App;

/// <summary>
/// Ctrl+Alt+Space with nothing selected (Settings → Shortcuts → Use the clipboard): copied files open the wheel as
/// they are; a copied image (a screenshot, a picture from a browser) is saved as a PNG first, and its results go to the
/// File Explorer folder in front, the desktop, or Pictures.
/// </summary>
public partial class App
{
    /// <summary>Files for the wheel from the clipboard, with where results should go (null: next to the files).</summary>
    private (FileSet Files, string? OutputFolder, string? Label)? FromClipboard(string? folderInFront)
    {
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                    if (files.Count > 0) return (new FileSet(files), null, null);
                }
                if (SaveClipboardImage() is { } capture)
                {
                    string target = folderInFront ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    var size = Dimensions(capture);
                    return (new FileSet([capture]), target, size is { } s ? $"Copied image · {s.W} × {s.H}" : "Copied image");
                }
                return null;
            }
            catch (COMException) when (attempt < 4) { Thread.Sleep(40 * attempt); } // another app has the clipboard open
            catch (Exception ex) { Log.Warn($"Reading the clipboard failed: {ex.Message}"); return null; }
        }
        return null;
    }

    /// <summary>Saves the clipboard image as PNG among the captures (keeping transparency when the app that copied it did).</summary>
    private string? SaveClipboardImage()
    {
        string folder = ClipboardCaptures.Folder!;
        Directory.CreateDirectory(folder);
        CleanOldCaptures(folder);
        string path = Path.Combine(folder, $"Copied image {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");

        // Browsers, Snipping Tool and most modern apps also put a real PNG on the clipboard: keep those exact bytes.
        if (Clipboard.GetData("PNG") is MemoryStream png)
        {
            try
            {
                BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); // it really is a PNG
                File.WriteAllBytes(path, png.ToArray());
                Log.Info("Clipboard PNG saved for the wheel");
                return path;
            }
            catch (Exception ex) { Log.Info($"Clipboard PNG unreadable ({ex.Message}); using the bitmap"); }
        }
        if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image) return null;

        // A plain bitmap (Print Screen, older apps) often says "fully transparent" for every pixel by mistake.
        BitmapSource frame = HasAlpha(image) && !AllTransparent(image) ? image : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) encoder.Save(fs);
        Log.Info($"Clipboard bitmap saved for the wheel ({image.PixelWidth} × {image.PixelHeight})");
        return path;
    }

    private static bool HasAlpha(BitmapSource image) => image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32;

    private static bool AllTransparent(BitmapSource image)
    {
        var bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var px = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(px, stride, 0);
        for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) return false;
        return true;
    }

    private static (int W, int H)? Dimensions(string png)
    {
        try { var f = BitmapFrame.Create(new Uri(png), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); return (f.PixelWidth, f.PixelHeight); }
        catch { return null; }
    }

    /// <summary>Captures are only stepping stones: anything older than a day goes.</summary>
    private static void CleanOldCaptures(string folder)
    {
        foreach (var f in Directory.EnumerateFiles(folder))
        {
            try { if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)) File.Delete(f); } catch { /* in use: next time */ }
        }
    }
}
