using System.Diagnostics;
using OrbitDrop.Core;

namespace OrbitDrop.Tools;

/// <summary>
/// File actions for anything you can drag, files and folders alike: copy path/name, show in folder, copy to and
/// move to a folder (never overwriting), share and open a terminal there. Copying and moving are the only actions
/// that write, and both pick a free name rather than replace anything.
/// </summary>
public sealed class FileActionsProvider(Func<IReadOnlyList<string>>? recentFolders = null, Action<string>? folderUsed = null) : IOperationProvider
{
    public const string ProviderId = "file.actions";
    public const string CopyToPrefix = "action.copy-to", MoveToPrefix = "action.move-to";
    /// <summary>Separates an action from the destination folder baked into its id ("action.copy-to|C:\Users\…").</summary>
    public const char FolderSeparator = '|';
    public const string ChooseSuffix = ".choose";
    public string Id => ProviderId;

    private static readonly OperationGroup CopyGroup = new("actions.copy", "Copy path", "\uE71B");
    private static readonly OperationGroup CopyToGroup = new("actions.copy-to", "Copy to", "\uE8C8");
    private static readonly OperationGroup MoveToGroup = new("actions.move-to", "Move to", "\uE8DE");

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (mode != OperationMode.Actions || files.IsEmpty) yield break;
        bool many = files.Count > 1;
        yield return new("action.copy-path", ProviderId, OperationMode.Actions, many ? "Copy paths" : "Copy path", "\uE71B",
            "%1", many ? "Copy paths" : "Copy path", "Copying path", 200, Group: CopyGroup, DoneTitle: many ? "Paths copied" : "Path copied");
        yield return new("action.copy-name", ProviderId, OperationMode.Actions, many ? "Copy names" : "Copy name", "\uE8AC",
            "%1", many ? "Copy names" : "Copy name", "Copying name", 201, Group: CopyGroup, DoneTitle: many ? "Names copied" : "Name copied");
        yield return new("action.reveal", ProviderId, OperationMode.Actions, "Show in folder", "\uE838",
            "%1", "Show in folder", "Opening folder", 205);

        foreach (var op in Destinations(CopyToPrefix, "Copy", "Copying", "Copied", CopyToGroup, 210)) yield return op;
        foreach (var op in Destinations(MoveToPrefix, "Move", "Moving", "Moved", MoveToGroup, 220)) yield return op;

        yield return new("action.float", ProviderId, OperationMode.Actions, "Float", "\uE718",
            "Keep on a shelf to drag out later", "", "Floating", 225, DoneTitle: "On the shelf");
        yield return new("action.share", ProviderId, OperationMode.Actions, "Share", "\uE72D",
            "%1", "Share", "Opening Share", 230, DoneTitle: "Share opened");
        yield return new("action.terminal", ProviderId, OperationMode.Actions, "Terminal", "\uE756",
            "Open a terminal here", "", "Opening Terminal", 240, DoneTitle: "Terminal opened");
    }

    /// <summary>"Choose…" (a folder picker) first as the default, then recent folders, then Desktop/Documents/Downloads.</summary>
    private IEnumerable<OperationDescriptor> Destinations(string prefix, string verb, string progress, string done, OperationGroup group, int order)
    {
        yield return new(prefix + ChooseSuffix, ProviderId, OperationMode.Actions, "Choose…", "\uE8DA",
            $"{verb} to a folder you choose", "", $"{progress} files", order, Group: group, NeedsInput: true, DoneTitle: $"{done}");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = (recentFolders?.Invoke() ?? []).Select(f => (Name: FolderName(f), Path: f))
            .Concat(KnownFolders.Common())
            .Where(f => Directory.Exists(f.Path) && seen.Add(Path.TrimEndingDirectorySeparator(f.Path)))
            .Take(6);
        foreach (var (name, path) in folders)
            yield return new($"{prefix}{FolderSeparator}{path}", ProviderId, OperationMode.Actions, name, null,
                $"{verb} to %1", name, $"{progress} to {name}", ++order, Group: group, DoneTitle: $"{done} to {name}");
    }

    private static string FolderName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } n ? n : path; // "D:\" has no name

    public async Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string id = operation.Id;
        if (id.StartsWith(CopyToPrefix, StringComparison.Ordinal) || id.StartsWith(MoveToPrefix, StringComparison.Ordinal))
        {
            bool move = id.StartsWith(MoveToPrefix, StringComparison.Ordinal);
            string folder = DestinationOf(operation, context) ?? throw new InvalidOperationException("No destination folder.");
            await Task.Run(() => Transfer(files, folder, move, progress, ct), ct);
            folderUsed?.Invoke(folder);
            return;
        }

        switch (id)
        {
            case "action.copy-path":
                context.Shell.SetClipboardText(string.Join(Environment.NewLine, files.Select(f => f.Contains(' ') ? $"\"{f}\"" : f)));
                break;
            case "action.copy-name":
                context.Shell.SetClipboardText(string.Join(Environment.NewLine, files.Select(f => Path.GetFileName(Path.TrimEndingDirectorySeparator(f)))));
                break;
            case "action.reveal":
                context.Shell.Reveal(files);
                break;
            case "action.float":
                context.Shell.Float(files);
                break;
            case "action.share":
                context.Shell.Share(files);
                break;
            case "action.terminal":
                OpenTerminal(Directory.Exists(files[0]) ? files[0] : Path.GetDirectoryName(files[0])!);
                break;
            default:
                throw new NotSupportedException(id);
        }
        foreach (var f in files) progress.Report(new FileOutcome(f, OutcomeStatus.Succeeded));
    }

    /// <summary>The folder baked into the id, or the one the user picked (passed as input).</summary>
    public static string? DestinationOf(OperationDescriptor operation, OperationContext context)
    {
        int bar = operation.Id.IndexOf(FolderSeparator);
        return bar >= 0 ? operation.Id[(bar + 1)..] : context.Input as string;
    }

    private static void Transfer(FileSet files, string folder, bool move, IProgress<FileOutcome> progress, CancellationToken ct)
    {
        foreach (var item in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(item)) ?? "";
                if (Directory.Exists(item) && FileTransfer.IsWithin(folder, item))
                {
                    progress.Report(new FileOutcome(item, OutcomeStatus.Failed, Message: "A folder can't go inside itself."));
                    continue;
                }
                if (move && string.Equals(Path.TrimEndingDirectorySeparator(parent), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
                {
                    progress.Report(new FileOutcome(item, OutcomeStatus.Skipped, Message: "It's already in that folder."));
                    continue;
                }
                string result = move ? FileTransfer.MoveInto(item, folder, ct) : FileTransfer.CopyInto(item, folder, ct);
                progress.Report(new FileOutcome(item, OutcomeStatus.Succeeded, result));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress.Report(new FileOutcome(item, OutcomeStatus.Failed, Message: ex switch
                {
                    FileNotFoundException or DirectoryNotFoundException => "It no longer exists, or the folder is gone.",
                    UnauthorizedAccessException => "Windows denied access (the file may be in use or the folder protected).",
                    IOException io when io.HResult == unchecked((int)0x80070070) => "There isn't enough disk space.",
                    IOException io when io.HResult == unchecked((int)0x80070020) => "The file is open in another app.",
                    _ => ex.Message,
                }));
            }
        }
    }

    /// <summary>Windows Terminal if it's installed, otherwise PowerShell, opened in <paramref name="folder"/>.</summary>
    private static void OpenTerminal(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", "-d .") { WorkingDirectory = folder, UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit") { WorkingDirectory = folder, UseShellExecute = true });
        }
    }

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("File actions", "Copy path or name, show in folder, copy to / move to a folder, share, open Terminal (files and folders)");
    }
}
