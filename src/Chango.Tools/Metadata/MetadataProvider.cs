using Chango.Core;

namespace Chango.Tools.Metadata;

/// <summary>
/// Metadata tools for any file: Info (view, and edit into a copy), Remove all (lossless, where supported) and
/// Remove location (GPS only, via the Windows property system). Originals are never changed.
/// </summary>
public sealed class MetadataProvider : IOperationProvider
{
    public const string ProviderId = "metadata";
    public const string InfoId = "tool.meta.info";
    public string Id => ProviderId;

    private static readonly OperationGroup Group = new("metadata", "Metadata", "");

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Tools || files.IsEmpty || !files.All(File.Exists)) yield break;
        // Info is one window per file, so it's offered for single files.
        if (files.Count == 1)
            yield return new(InfoId, ProviderId, OperationMode.Tools, "Info", "", "See and edit details", "",
                "Saving details", 150, Group: Group, NeedsInput: true);
        if (files.All(MetadataStripper.CanStrip))
            yield return new("tool.meta.strip", ProviderId, OperationMode.Tools, "Remove all", "", "Remove all metadata", "",
                "Removing metadata", 151, Group: Group);
        if (files.All(f => FileProperties.LocationCapable.Contains(Path.GetExtension(f))))
            yield return new("tool.meta.location", ProviderId, OperationMode.Tools, "Remove location", "", "Remove GPS location", "",
                "Removing location", 152, Group: Group);
    }

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string output = operation.Id switch
                {
                    "tool.meta.strip" => await Task.Run(() => SafeOutput.Write(file, context.Output, null, "no metadata",
                        s => MetadataStripper.Strip(file, s), _ => null, ct), ct),
                    "tool.meta.location" => await EditCopyAsync(file, new MetadataEdit(new Dictionary<string, string?>(), true), "no location", context, ct),
                    InfoId when context.Input is MetadataEdit edit => await EditCopyAsync(file, edit, "edited", context, ct),
                    _ => throw new NotSupportedException(operation.Id),
                };
                progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, output));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex, file)));
            }
        }
    }

    /// <summary>Copies the file and applies the edit to the copy, with SafeOutput's guarantees.</summary>
    private static Task<string> EditCopyAsync(string file, MetadataEdit edit, string suffix, OperationContext context, CancellationToken ct) =>
        Task.Run(() => SafeOutput.WriteToPath(file, context.Output, null, suffix,
            writeToTemp: temp =>
            {
                if (edit.RemoveLocation) File.WriteAllBytes(temp, MetadataStripper.JpegWithoutLocation(File.ReadAllBytes(file)));
                else File.Copy(file, temp, overwrite: true);
                File.SetAttributes(temp, FileAttributes.Normal); // a read-only original would make the copy read-only too
                if (edit.Changes.Count > 0) FileProperties.WriteAsync(temp, edit).GetAwaiter().GetResult();
            },
            validate: _ => null, ct, hideWhileWriting: false, tempKeepsExtension: true), ct);

    private static string Friendly(Exception ex, string file) => ex switch
    {
        FileNotFoundException => "The file no longer exists.",
        UnauthorizedAccessException => "Windows denied access (read-only folder or file in use).",
        InvalidDataException => ex.Message,
        NotSupportedException => "That can't be done for this file.",
        _ when ex.HResult == unchecked((int)0x80030005) || ex.HResult == unchecked((int)0x80070005) => "Windows denied access to the file.",
        _ => $"Windows can't change the details of {Path.GetExtension(file).TrimStart('.').ToUpperInvariant()} files ({ex.Message.Trim()})",
    };

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Metadata", "Info for any file · edit details (photos, music, video, Office) · remove all (JPG, PNG, MP3, FLAC) · remove location (JPG, TIFF, HEIC)");
    }
}
