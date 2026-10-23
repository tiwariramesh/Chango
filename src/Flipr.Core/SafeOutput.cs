using System.Security.Cryptography;

namespace Flipr.Core;

/// <summary>
/// File-integrity guarantees for every output Flipr writes:
/// <list type="bullet">
/// <item>the original is never opened for writing;</item>
/// <item>output is written to a hidden temp file in the destination folder, then validated;</item>
/// <item>it is published with an atomic rename that never replaces an existing file (collision → " (2)", " (3)" …);</item>
/// <item>on any failure or cancellation the temp file is deleted, so no partial output is ever visible.</item>
/// </list>
/// </summary>
public static class SafeOutput
{
    public const string TempPrefix = ".flipr-";
    private const int MaxAttempts = 999;

    /// <param name="newExtension">Extension including the dot; null keeps the source extension.</param>
    /// <param name="suffix">Tool suffix: "rotated" → "photo (rotated).jpg".</param>
    /// <param name="validate">Returns null if the temp file is valid, otherwise a reason.</param>
    public static string Write(string source, OutputOptions options, string? newExtension, string? suffix,
        Action<Stream> write, Func<string, string?> validate, CancellationToken cancellationToken = default) =>
        WriteToPath(source, options, newExtension, suffix, temp =>
        {
            using var stream = new FileStream(temp, FileMode.Open, FileAccess.Write, FileShare.None);
            write(stream);
        }, validate, cancellationToken);

    /// <summary>
    /// Same guarantees as <see cref="Write"/>, for engines that must open the output themselves (e.g. Media Foundation).
    /// <paramref name="writeToTemp"/> receives the path of an existing, empty temp file to fill. Set <paramref name="hideWhileWriting"/>
    /// to false for Windows storage APIs, which refuse to open hidden files; the file is hidden as soon as it is written.
    /// Set <paramref name="tempKeepsExtension"/> when the writer needs the real extension (e.g. Windows property handlers).
    /// </summary>
    public static string WriteToPath(string source, OutputOptions options, string? newExtension, string? suffix,
        Action<string> writeToTemp, Func<string, string?> validate, CancellationToken cancellationToken = default,
        bool hideWhileWriting = true, bool tempKeepsExtension = false)
    {
        string dir = options.DirectoryFor(source);
        Directory.CreateDirectory(dir);
        string ext = newExtension ?? Path.GetExtension(source);
        string baseName = Path.GetFileNameWithoutExtension(source) + (suffix is null ? "" : $" ({suffix})");
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

            for (int n = 1; n <= MaxAttempts; n++)
            {
                string candidate = Path.Combine(dir, n == 1 ? $"{baseName}{ext}" : $"{baseName} ({n}){ext}");
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
    /// The folder equivalent of <see cref="WriteToPath"/>: <paramref name="fill"/> populates a hidden temp folder in
    /// the destination, which is then renamed to <paramref name="folderName"/> (or "name (2)" …). Nothing partial is
    /// ever visible, and an existing folder is never merged into or replaced.
    /// </summary>
    public static string WriteFolder(string source, OutputOptions options, string folderName, Action<string> fill,
        CancellationToken cancellationToken = default)
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

            for (int n = 1; n <= MaxAttempts; n++)
            {
                string candidate = Path.Combine(dir, n == 1 ? folderName : $"{folderName} ({n})");
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                try { Directory.Move(temp, candidate); }
                catch (IOException) when (Directory.Exists(candidate) || File.Exists(candidate)) { continue; }
                var published = new DirectoryInfo(candidate);
                published.Attributes &= ~FileAttributes.Hidden;
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
