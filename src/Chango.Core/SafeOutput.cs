using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Chango.Core;

/// <summary>
/// File-integrity guarantees for every output Chango writes:
/// <list type="bullet">
/// <item>the original is never opened for writing;</item>
/// <item>output is written to a hidden temp file in the destination folder, then validated;</item>
/// <item>it is published with an atomic rename that never replaces an existing file;</item>
/// <item>on any failure or cancellation the temp file is deleted, so no partial output is ever visible;</item>
/// <item>the result keeps the original's created and modified dates (<see cref="OutputOptions.KeepSourceDates"/>).</item>
/// </list>
/// Naming: a conversion keeps the name ("photo.jpg" → "photo.png"); a tool's result, or a conversion whose name is
/// taken, is numbered: "photo.jpg" → "photo_1.jpg", and "photo_1.jpg" → "photo_2.jpg" (see <see cref="CandidateNames"/>).
/// </summary>
public static partial class SafeOutput
{
    public const string TempPrefix = ".chango-";
    private const int MaxAttempts = 999;

    /// <param name="newExtension">Extension including the dot; null keeps the source extension.</param>
    /// <param name="numbered">A tool's result ("photo.jpg" → "photo_1.jpg"); false for conversions ("photo.png").</param>
    /// <param name="validate">Returns null if the temp file is valid, otherwise a reason.</param>
    public static string Write(string source, OutputOptions options, string? newExtension, bool numbered,
        Action<Stream> write, Func<string, string?> validate, CancellationToken cancellationToken = default) =>
        WriteToPath(source, options, newExtension, numbered, temp =>
        {
            using var stream = new FileStream(temp, FileMode.Open, FileAccess.Write, FileShare.None);
            write(stream);
        }, validate, cancellationToken);

    /// <summary>
    /// Same guarantees as <see cref="Write"/>, for engines that must open the output themselves (e.g. Media Foundation).
    /// <paramref name="writeToTemp"/> receives the path of an existing, empty temp file to fill. Set <paramref name="hideWhileWriting"/>
    /// to false for Windows storage APIs, which refuse to open hidden files; the file is hidden as soon as it is written.
    /// Set <paramref name="tempKeepsExtension"/> when the writer needs the real extension (e.g. Windows property handlers).
    /// <paramref name="copyNames"/> names a taken name the way File Explorer does for copies ("photo (2).png").
    /// </summary>
    public static string WriteToPath(string source, OutputOptions options, string? newExtension, bool numbered,
        Action<string> writeToTemp, Func<string, string?> validate, CancellationToken cancellationToken = default,
        bool hideWhileWriting = true, bool tempKeepsExtension = false, bool copyNames = false)
    {
        string dir = options.DirectoryFor(source);
        Directory.CreateDirectory(dir);
        string ext = newExtension ?? Path.GetExtension(source);
        // Windows picks property handlers by extension, so metadata edits need the temp to keep the real one.
        string temp = Path.Combine(dir, $"{TempPrefix}{Guid.NewGuid():N}{(tempKeepsExtension ? ext : ".tmp")}");

        try
        {
            using (new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { } // reserve the name
            if (hideWhileWriting) File.SetAttributes(temp, FileAttributes.Hidden);
            writeToTemp(temp);
            if (!hideWhileWriting && File.Exists(temp)) File.SetAttributes(temp, FileAttributes.Hidden);
            cancellationToken.ThrowIfCancellationRequested();

            if (new FileInfo(temp).Length == 0) throw new IOException("The output was empty.");
            if (validate(temp) is { } problem) throw new IOException($"The output failed validation: {problem}");
            cancellationToken.ThrowIfCancellationRequested();

            string stem = Path.GetFileNameWithoutExtension(source);
            var names = copyNames ? CopyNames(stem, ext) : CandidateNames(stem, ext, numbered);
            foreach (var name in names)
            {
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                try
                {
                    File.Move(temp, candidate, overwrite: false); // MoveFileEx without REPLACE_EXISTING
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    continue; // another writer won the race for this name
                }
                File.SetAttributes(candidate, File.GetAttributes(candidate) & ~FileAttributes.Hidden);
                if (options.KeepSourceDates) CopyDates(source, candidate);
                OutputLedger.Record(candidate);
                return candidate;
            }
            throw new IOException("No free output file name was found.");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The names to try, in order. A conversion tries its own name first ("photo.png"); after that, and for every
    /// tool result, the name is numbered after the source's: "photo" → "photo_1", "photo_1" → "photo_2", skipping
    /// numbers already taken. Only _1 … _99 without a leading zero counts as a number Chango gave, so camera names
    /// such as "IMG_1234" or "DSC_0001" are never mistaken for one ("IMG_1234" → "IMG_1234_1").
    /// </summary>
    public static IEnumerable<string> CandidateNames(string stem, string extension, bool numbered)
    {
        if (!numbered) yield return stem + extension;
        var match = ChangoNumber().Match(stem);
        (string baseName, int first) = match.Success
            ? (match.Groups["base"].Value, int.Parse(match.Groups["n"].Value) + 1)
            : (stem, 1);
        for (int n = first; n < first + MaxAttempts; n++) yield return $"{baseName}_{n}{extension}";
    }

    /// <summary>"photo.png", then "photo (2).png", "photo (3).png" …: File Explorer's names for a copy.</summary>
    public static IEnumerable<string> CopyNames(string stem, string extension)
    {
        yield return stem + extension;
        for (int n = 2; n <= MaxAttempts; n++) yield return $"{stem} ({n}){extension}";
    }

    [GeneratedRegex(@"^(?<base>.+)_(?<n>[1-9][0-9]?)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChangoNumber();

    /// <summary>
    /// Gives <paramref name="target"/> the created and modified dates of <paramref name="source"/>, when the source is
    /// a real file (archives name their result after a path that doesn't exist, and keep today's date).
    /// </summary>
    public static void CopyDates(string source, string target)
    {
        try
        {
            if (!File.Exists(source)) return;
            var from = new FileInfo(source);
            if (File.Exists(target))
            {
                File.SetCreationTimeUtc(target, from.CreationTimeUtc);
                File.SetLastWriteTimeUtc(target, from.LastWriteTimeUtc);
            }
            else if (Directory.Exists(target))
            {
                Directory.SetCreationTimeUtc(target, from.CreationTimeUtc);
                Directory.SetLastWriteTimeUtc(target, from.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the result is fine; only its dates are new */ }
    }

    /// <summary>
    /// The folder equivalent of <see cref="WriteToPath"/>: <paramref name="fill"/> populates a hidden temp folder in
    /// the destination, which is then renamed to <paramref name="folderName"/> (or "name (2)" …). Nothing partial is
    /// ever visible, and an existing folder is never merged into or replaced. With <paramref name="dateContents"/>,
    /// the folder and every file in it get the source's dates (pages made from a document); extracted archives keep
    /// the dates their entries carry.
    /// </summary>
    public static string WriteFolder(string source, OutputOptions options, string folderName, Action<string> fill,
        CancellationToken cancellationToken = default, bool dateContents = false)
    {
        string dir = options.DirectoryFor(source);
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, $"{TempPrefix}{Guid.NewGuid():N}");
        try
        {
            var info = Directory.CreateDirectory(temp);
            info.Attributes |= FileAttributes.Hidden;
            fill(temp);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.EnumerateFileSystemEntries(temp).Any()) throw new IOException("Nothing was produced.");
            bool date = dateContents && options.KeepSourceDates;
            if (date)
                foreach (var file in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories)) CopyDates(source, file);

            for (int n = 1; n <= MaxAttempts; n++)
            {
                string candidate = Path.Combine(dir, n == 1 ? folderName : $"{folderName} ({n})");
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                try { Directory.Move(temp, candidate); }
                catch (IOException) when (Directory.Exists(candidate) || File.Exists(candidate)) { continue; }
                var published = new DirectoryInfo(candidate);
                published.Attributes &= ~FileAttributes.Hidden;
                if (date) CopyDates(source, candidate);
                OutputLedger.Record(candidate);
                return candidate;
            }
            throw new IOException("No free folder name was found.");
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
        }
    }

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static IEnumerable<string> LeftoverTemps(string directory) =>
        Directory.EnumerateFiles(directory, $"{TempPrefix}*");
}
