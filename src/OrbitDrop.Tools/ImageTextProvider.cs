using System.Runtime.InteropServices;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace OrbitDrop.Tools;

/// <summary>
/// Copy text from an image with Windows' built-in text recognition (Windows.Media.Ocr, offline, in the languages
/// installed on this PC), and set an image as the desktop wallpaper.
/// </summary>
public sealed class ImageTextProvider : IOperationProvider
{
    public const string ProviderId = "image.text";
    public string Id => ProviderId;

    /// <summary>
    /// Text recognition and the wallpaper both hand the file to Windows itself, so only formats a Windows codec reads
    /// qualify: not SVG (drawn by OrbitDrop), and WebP only where Microsoft's WebP extension is installed.
    /// </summary>
    private static bool WindowsCanRead(string path)
    {
        if (!WicImageCodec.IsSupportedInput(path)) return false;
        var decoders = WicCodecs.DecoderExtensions;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return decoders.Count > 0 ? decoders.Contains(ext) : ext is not (".svg" or ".webp");
    }

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (!files.All(WindowsCanRead)) yield break;
        if (mode == OperationMode.Tools)
            yield return new("tool.image.ocr", ProviderId, OperationMode.Tools, "Copy text", "",
                "Copy the text in the image", "", "Reading text", 146, DoneTitle: "Text copied");
        else if (mode == OperationMode.Actions && files.Count == 1)
            yield return new("action.wallpaper", ProviderId, OperationMode.Actions, "Wallpaper", "",
                "Set as desktop wallpaper", "", "Setting wallpaper", 245, DoneTitle: "Wallpaper set");
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        if (operation.Id == "action.wallpaper")
        {
            bool ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, files[0], SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            progress.Report(new FileOutcome(files[0], ok ? OutcomeStatus.Succeeded : OutcomeStatus.Failed,
                Message: ok ? null : "Windows didn't accept this image as a wallpaper."));
            return;
        }

        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No text-recognition language is installed (Settings → Time & language → Language).");
        var texts = new List<string>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string text = await RecognizeAsync(engine, file);
                if (string.IsNullOrWhiteSpace(text))
                    progress.Report(new FileOutcome(file, OutcomeStatus.Skipped, Message: "No text was found in this image."));
                else
                {
                    texts.Add(text);
                    progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: ex.Message));
            }
        }
        if (texts.Count > 0) context.Shell.SetClipboardText(string.Join(Environment.NewLine + Environment.NewLine, texts));
    }

    public static async Task<string> RecognizeAsync(OcrEngine engine, string file)
    {
        var storage = await StorageFile.GetFileFromPathAsync(file);
        using var stream = await storage.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        // OCR has a size limit; scale large photos down to it (text stays readable).
        uint max = OcrEngine.MaxImageDimension, w = decoder.OrientedPixelWidth, h = decoder.OrientedPixelHeight;
        var transform = new BitmapTransform();
        if (Math.Max(w, h) > max)
        {
            double k = (double)max / Math.Max(w, h);
            transform.ScaledWidth = (uint)(w * k);
            transform.ScaledHeight = (uint)(h * k);
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
    }

    private const uint SPI_SETDESKWALLPAPER = 0x0014, SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Text in images", "Copy text (offline text recognition, in your Windows languages) · set as wallpaper");
    }
}
