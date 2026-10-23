namespace Chango.Core;

/// <summary>The three wheel modes, cycled by tapping the trigger key during a drag.</summary>
public enum OperationMode { Convert, Tools, Actions }

/// <summary>
/// A single wheel option. Providers create descriptors; the UI only renders them.
/// </summary>
/// <param name="Id">Stable identifier, e.g. "convert.image.png", "tool.image.rotate-right", "action.copy-path".</param>
/// <param name="Label">Segment label ("PNG", "Rotate right").</param>
/// <param name="Glyph">Optional Segoe Fluent Icons glyph for tool/action segments; format segments are text-only.</param>
/// <param name="DropText">Native drag tooltip, with %1 replaced by <paramref name="DropInsert"/> ("Convert to %1").</param>
/// <param name="ProgressTitle">Toast title while running ("Converting to PNG").</param>
/// <param name="NeedsInput">The app asks for choices first (a window or picker) and passes them as <see cref="OperationContext.Input"/>.</param>
/// <param name="DoneTitle">Toast title when an action finishes ("Copied to Desktop"); defaults to the label.</param>
public sealed record OperationDescriptor(
    string Id,
    string ProviderId,
    OperationMode Mode,
    string Label,
    string? Glyph,
    string DropText,
    string DropInsert,
    string ProgressTitle,
    int Order,
    bool MirrorGlyph = false,
    OperationGroup? Group = null,
    bool NeedsInput = false,
    string? DoneTitle = null)
{
    public const string GroupPrefix = "group.";

    /// <summary>True for the synthetic segment that stands for a collapsed <see cref="OperationGroup"/>.</summary>
    public bool IsGroupHeader => Id.StartsWith(GroupPrefix, StringComparison.Ordinal);
}

/// <summary>
/// Related options that share one wheel segment ("Compress" → 75%, 50%, Under 1 MB …). Hovering the segment opens
/// them as a sub-wheel; dropping on it runs the first (default) option.
/// </summary>
public sealed record OperationGroup(string Id, string Label, string? Glyph, bool MirrorGlyph = false);

public enum OutcomeStatus { Succeeded, Skipped, Failed, Canceled }

/// <summary>Result for one input file. <see cref="Output"/> is set when a new file was written.</summary>
public sealed record FileOutcome(string Source, OutcomeStatus Status, string? Output = null, string? Message = null);

public enum OutputLocation { BesideOriginal, Folder }

public sealed record OutputOptions(OutputLocation Location = OutputLocation.BesideOriginal, string? Folder = null)
{
    public string DirectoryFor(string source) =>
        Location == OutputLocation.Folder && !string.IsNullOrWhiteSpace(Folder)
            ? Folder!
            : Path.GetDirectoryName(source) ?? throw new IOException($"'{source}' has no parent directory");
}

/// <summary>Services and preferences available to providers while executing.</summary>
public sealed record OperationContext
{
    public required OutputOptions Output { get; init; }
    public int JpegQuality { get; init; } = 92;
    public required IShellIntegration Shell { get; init; }

    /// <summary>Progress (0–1) within the file currently being processed; set by the job queue. Optional for providers.</summary>
    public Action<double>? ReportFileFraction { get; init; }

    /// <summary>Choices collected interactively before the job started (e.g. a trim range or edited metadata). Provider-specific.</summary>
    public object? Input { get; init; }
}

/// <summary>Windows shell services a provider may need, implemented in Chango.Windows.</summary>
public interface IShellIntegration
{
    void SetClipboardText(string text);
    void Reveal(IReadOnlyList<string> paths);

    /// <summary>Opens the Windows share window (Nearby Share, Mail, apps …) for the files.</summary>
    void Share(IReadOnlyList<string> paths) => throw new NotSupportedException("Sharing isn't available here.");

    /// <summary>Parks the files on the floating shelf to drag out later.</summary>
    void Float(IReadOnlyList<string> paths) => throw new NotSupportedException("The shelf isn't available here.");

    /// <summary>Sends files and folders to the Recycle Bin (never a permanent delete). Returns how many went.</summary>
    int Recycle(IReadOnlyList<string> paths) => throw new NotSupportedException("The Recycle Bin isn't available here.");
}

/// <summary>
/// A plugin that contributes wheel options and executes them. Contributors add capabilities by adding providers;
/// nothing in the wheel, jobs or app needs to change.
/// </summary>
public interface IOperationProvider
{
    string Id { get; }

    /// <summary>Options valid for <b>every</b> file in <paramref name="files"/> in the given mode. Must be cheap (no file I/O).</summary>
    IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode);

    /// <summary>
    /// Executes <paramref name="operation"/>, reporting exactly one <see cref="FileOutcome"/> per input file.
    /// Must honour <paramref name="cancellationToken"/> between files and never modify the inputs.
    /// </summary>
    Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken cancellationToken);

    /// <summary>Human-readable capability summary for Settings, e.g. ("Images", "PNG, JPG, BMP, TIFF, GIF").</summary>
    IEnumerable<(string Category, string Detail)> DescribeCapabilities() => [];
}

/// <summary>
/// Optional for providers: the likely size of a result before it's made, for the wheel's "≈ 640 KB · −73%" hint
/// (Settings → Wheel → Show estimated size). Runs in the background while the wheel is open; must be read-only.
/// </summary>
public interface IEstimatesSize
{
    /// <summary>(operation id, estimated bytes) for the options this provider can estimate for <paramref name="file"/>.</summary>
    IEnumerable<(string OperationId, long Bytes)> EstimateSizes(string file, OperationContext context);
}
