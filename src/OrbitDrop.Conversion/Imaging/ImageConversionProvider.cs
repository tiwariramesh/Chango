using OrbitDrop.Core;

namespace OrbitDrop.Conversion.Imaging;

/// <summary>Image format conversion: PNG, JPG, WebP, BMP, TIFF, GIF, JFIF ↔ each other (WIC, plus Skia for WebP).</summary>
public sealed class ImageConversionProvider : IOperationProvider, IEstimatesSize
{
    public const string ProviderId = "image.convert";
    public string Id => ProviderId;

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Convert || !files.All(WicImageCodec.IsSupportedInput)) yield break;

        // Hide a target only when every file is already in it; mixed selections keep it and skip per file.
        var sourceFormats = files.Select(f => WicImageCodec.FormatOf(f)!.Name).Distinct().ToList();
        // A copied image is saved as PNG first; "PNG" then simply saves it, so it stays on the wheel.
        bool captures = files.All(ClipboardCaptures.IsCapture);
        int order = 0;
        foreach (var format in WicImageCodec.OutputFormats)
        {
            order++;
            if (sourceFormats.Count == 1 && sourceFormats[0] == format.Name && !captures) continue;
            yield return new OperationDescriptor(
                Id: $"convert.image.{format.Name.ToLowerInvariant()}",
                ProviderId: ProviderId,
                Mode: OperationMode.Convert,
                Label: format.Name,
                Glyph: null,
                DropText: "Convert to %1",
                DropInsert: format.Name,
                ProgressTitle: $"Converting to {format.Name}",
                Order: order);
        }
    }

    public Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        var target = WicImageCodec.OutputFormats.Single(f => operation.Id == $"convert.image.{f.Name.ToLowerInvariant()}");
        return StaWorker.RunAsync(() =>
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (WicImageCodec.FormatOf(file)?.Name == target.Name && ClipboardCaptures.IsCapture(file))
                {
                    progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, Rename(file, target, context, ct)));
                    continue;
                }
                if (WicImageCodec.FormatOf(file)?.Name == target.Name)
                {
                    progress.Report(new FileOutcome(file, OutcomeStatus.Skipped, Message: $"Already {target.Name}"));
                    continue;
                }
                try
                {
                    string output = IsJpeg(WicImageCodec.FormatOf(file)) && IsJpeg(target)
                        ? Rename(file, target, context, ct)
                        : WicImageCodec.WriteOutput(WicImageCodec.Load(file), file, target, suffix: null, context, ct);
                    progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex)));
                }
            }
        }, "OrbitDrop image conversion");
    }

    private static bool IsJpeg(ImageFormat? f) => f == WicImageCodec.Jpg || f == WicImageCodec.Jfif;

    /// <summary>JPG ↔ JFIF is the same file under another name: copy it byte for byte, so nothing is re-compressed.</summary>
    private static string Rename(string file, ImageFormat target, OperationContext context, CancellationToken ct) =>
        SafeOutput.Write(file, context.Output, target.Extension, suffix: null,
            write: s => { using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); src.CopyTo(s); },
            validate: temp => new FileInfo(temp).Length == new FileInfo(file).Length ? null : "the copy is incomplete",
            ct);

    public IEnumerable<(string OperationId, long Bytes)> EstimateSizes(string file, OperationContext context)
    {
        if (!WicImageCodec.IsSupportedInput(file)) return [];
        return SizeEstimate.Formats(file, context.JpegQuality).Select(p => ($"convert.image.{p.Key.ToLowerInvariant()}", p.Value)).ToList();
    }

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Images", string.Join(", ", WicImageCodec.OutputFormats.Select(f => f.Name)) + " (convert between any of these)");
    }

    public static string Friendly(Exception ex) => ex switch
    {
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        NotSupportedException or FileFormatException or InvalidDataException => "The file isn't a readable image.",
        IOException io when io.HResult == unchecked((int)0x80070070) => "There isn't enough disk space.",
        _ => ex.Message,
    };
}
