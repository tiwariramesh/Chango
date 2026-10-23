using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitDrop.Conversion.Imaging;
using OrbitDrop.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace OrbitDrop.Conversion.Media;

/// <summary>
/// Video editing through Windows.Media.Editing (built into Windows): a still frame as PNG, and joining clips into one
/// MP4 in the order they were selected.
/// </summary>
public sealed class MediaEditingProvider : IOperationProvider
{
    public const string ProviderId = "media.editing";
    public string Id => ProviderId;

    /// <summary>GIFs cover the first <see cref="GifSeconds"/> s at <see cref="GifFps"/> fps, at most <see cref="GifSide"/> px.</summary>
    public const int GifSeconds = 10, GifFps = 10, GifSide = 480;

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (!files.All(MediaFoundationProvider.IsVideo)) yield break;
        if (mode == OperationMode.Convert)
            yield return new("convert.video.gif", ProviderId, OperationMode.Convert, "GIF", null, "Animated %1 (first 10 s)", "GIF", "Making GIF", 23);
        if (mode != OperationMode.Tools) yield break;
        yield return new("tool.video.snapshot", ProviderId, OperationMode.Tools, "Snapshot", "",
            "Save the middle frame as PNG", "", "Taking a snapshot", 135);
        if (files.Count > 1)
            yield return new("tool.video.join", ProviderId, OperationMode.Tools, "Join", "",
                "Join into one video, in this order", "", "Joining videos", 136);
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        if (operation.Id == "tool.video.join")
        {
            try
            {
                string output = await JoinAsync(files, context, ct);
                for (int i = 0; i < files.Count; i++) progress.Report(new FileOutcome(files[i], OutcomeStatus.Succeeded, i == 0 ? output : null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                foreach (var f in files) progress.Report(new FileOutcome(f, OutcomeStatus.Failed, Message: Friendly(ex)));
            }
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string output = operation.Id == "convert.video.gif" ? await GifAsync(file, context, ct) : await SnapshotAsync(file, context, ct);
                progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex))); }
        }
    }

    private static async Task<string> SnapshotAsync(string file, OperationContext context, CancellationToken ct)
    {
        var storage = await StorageFile.GetFileFromPathAsync(file);
        var clip = await MediaClip.CreateFromFileAsync(storage);
        var props = await storage.Properties.GetVideoPropertiesAsync();
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        var at = TimeSpan.FromTicks(clip.OriginalDuration.Ticks / 2);
        int w = (int)(props.Width > 0 ? props.Width : 1280), h = (int)(props.Height > 0 ? props.Height : 720);
        using var thumb = await composition.GetThumbnailAsync(at, w, h, VideoFramePrecision.NearestFrame).AsTask(ct);
        using var source = thumb.AsStreamForRead();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        byte[] data = buffer.ToArray();

        string output = "";
        await StaWorker.RunAsync(() =>
        {
            using var ms = new MemoryStream(data);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            BitmapSource image = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            output = WicImageCodec.WriteOutput(image, file, WicImageCodec.Png, $"frame {Stamp(at)}", context, ct);
        }, "OrbitDrop snapshot");
        return output;
    }

    /// <summary>Samples frames with the composition's thumbnail engine and writes a looping animated GIF.</summary>
    private static async Task<string> GifAsync(string file, OperationContext context, CancellationToken ct)
    {
        var storage = await StorageFile.GetFileFromPathAsync(file);
        var props = await storage.Properties.GetVideoPropertiesAsync();
        var composition = new MediaComposition();
        var clip = await MediaClip.CreateFromFileAsync(storage);
        composition.Clips.Add(clip);
        double seconds = Math.Min(GifSeconds, clip.OriginalDuration.TotalSeconds);
        int count = Math.Max(1, (int)Math.Floor(seconds * GifFps));
        var times = Enumerable.Range(0, count).Select(i => TimeSpan.FromMilliseconds(i * 1000.0 / GifFps)).ToList();
        var (w, h) = MediaFoundationProvider.FitWithin(props.Width > 0 ? props.Width : 640, props.Height > 0 ? props.Height : 360, GifSide, GifSide);

        var frames = new List<byte[]>();
        var thumbs = await composition.GetThumbnailsAsync(times, (int)w, (int)h, VideoFramePrecision.NearestFrame).AsTask(ct);
        foreach (var thumb in thumbs)
        {
            using (thumb)
            using (var s = thumb.AsStreamForRead())
            using (var ms = new MemoryStream())
            {
                await s.CopyToAsync(ms, ct);
                frames.Add(ms.ToArray());
            }
            context.ReportFileFraction?.Invoke(frames.Count * 0.8 / count);
        }

        byte[] gif = [];
        await StaWorker.RunAsync(() =>
        {
            var encoder = new GifBitmapEncoder();
            foreach (var data in frames)
            {
                BitmapFrame frame;
                try
                {
                    using var ms = new MemoryStream(data);
                    frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                }
                catch (Exception) { continue; } // an empty sample (e.g. past the last frame): skip it
                // GIF is 8-bit: give each frame its own 256-colour palette computed from that frame.
                BitmapSource rgb = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
                var indexed = new FormatConvertedBitmap(rgb, PixelFormats.Indexed8, new BitmapPalette(rgb, 256), 0);
                encoder.Frames.Add(BitmapFrame.Create(indexed)); // timing is added afterwards: WPF ignores GIF frame metadata
            }
            if (encoder.Frames.Count < 2) throw new InvalidDataException("Windows couldn't read enough frames from this video to make a GIF.");
            using var output = new MemoryStream();
            encoder.Save(output);
            gif = GifLoop.AddTimingAndLoop(output.ToArray(), 100 / GifFps);
        }, "OrbitDrop GIF");

        return SafeOutput.Write(file, context.Output, ".gif", null, s => s.Write(gif),
            temp => File.ReadAllBytes(temp) is var b && b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' ? null : "the GIF is invalid", ct);
    }

    private static string Stamp(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\.mm\.ss") : t.ToString(@"m\.ss");

    private static async Task<string> JoinAsync(FileSet files, OperationContext context, CancellationToken ct)
    {
        var composition = new MediaComposition();
        uint width = 0, height = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var storage = await StorageFile.GetFileFromPathAsync(file);
            composition.Clips.Add(await MediaClip.CreateFromFileAsync(storage));
            if (width == 0)
            {
                var props = await storage.Properties.GetVideoPropertiesAsync();
                (width, height) = MediaFoundationProvider.FitWithin(props.Width, props.Height, 3840, 2160);
            }
        }

        // The composition's own profile carries everything its renderer needs (frame rate, pixel aspect …);
        // a hand-built one fails with MF_E_ATTRIBUTENOTFOUND. Only the size and bitrate are adjusted.
        var profile = composition.CreateDefaultEncodingProfile();
        if (width > 0 && height > 0 && profile.Video is not null)
        {
            profile.Video.Width = width;
            profile.Video.Height = height;
            profile.Video.Bitrate = (uint)Math.Clamp(width * height * 4.0, 1_000_000, 16_000_000);
        }

        return await Task.Run(() => SafeOutput.WriteToPath(files[0], context.Output, ".mp4", "joined",
            writeToTemp: temp =>
            {
                var destination = StorageFile.GetFileFromPathAsync(temp).AsTask().GetAwaiter().GetResult();
                var progress = new Progress<double>(p => context.ReportFileFraction?.Invoke(p / 100.0));
                var result = composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile).AsTask(ct, progress).GetAwaiter().GetResult();
                if (result != TranscodeFailureReason.None) throw new InvalidDataException("Windows couldn't join these videos.");
            },
            validate: temp => MediaFoundationProvider.ValidateContainer(temp, ".mp4"),
            ct, hideWhileWriting: false, tempKeepsExtension: true), ct);
    }

    private static string Friendly(Exception ex) => ex switch
    {
        InvalidDataException => ex.Message,
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        _ when ex.HResult == unchecked((int)0xC00D36C4) => "Windows doesn't support this video's format.",
        _ => $"Windows reported an error (0x{ex.HResult:X8}{(string.IsNullOrWhiteSpace(ex.Message) ? "" : ": " + ex.Message.Trim())})",
    };

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Video editing", "Snapshot (middle frame as PNG) · join videos into one MP4");
    }
}
