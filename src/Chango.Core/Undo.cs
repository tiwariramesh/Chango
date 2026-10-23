namespace Chango.Core;

/// <summary>
/// What "Undo" may take back after a job: only files and folders the job itself created. Never an original, never
/// the result of "Move to" (that's the user's own file in its new place), never anything older than the job.
/// The app sends what this returns to the Recycle Bin, so even an undo can be undone.
/// </summary>
public static class Undo
{
    /// <summary>Some file systems round creation times; allow a little slack before the job started.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(2);

    public static bool IsUndoable(OperationDescriptor operation) =>
        !operation.Id.StartsWith("action.move-to", StringComparison.Ordinal);

    public static IReadOnlyList<string> Targets(OperationDescriptor operation, IReadOnlyList<string> sources,
        IEnumerable<FileOutcome> outcomes, DateTime jobCreatedUtc)
    {
        if (!IsUndoable(operation)) return [];
        var originals = new HashSet<string>(sources.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var o in outcomes)
        {
            if (o.Status != OutcomeStatus.Succeeded || o.Output is not { Length: > 0 } output) continue;
            string path = Normalize(output);
            if (originals.Contains(path)) continue;
            DateTime created;
            if (File.Exists(path)) created = File.GetCreationTimeUtc(path);
            else if (Directory.Exists(path)) created = Directory.GetCreationTimeUtc(path);
            else continue; // already gone
            if (created < jobCreatedUtc - Slack) continue; // older than the job: not ours to remove
            if (!result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
        }
        return result;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
