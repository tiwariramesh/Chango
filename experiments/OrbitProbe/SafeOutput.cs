using System.IO;
using System.Security.Cryptography;

namespace OrbitProbe;

/// <summary>
/// Collision-safe, atomic output beside the original:
/// write hidden temp in the destination folder → validate → rename without replace → never overwrite.
/// </summary>
public static class SafeOutput
{
    public const string TempPrefix = ".chango-";

    /// <param name="source">Original file (never opened for writing).</param>
    /// <param name="newExtension">Extension incl. dot; null keeps the source extension.</param>
    /// <param name="suffix">Optional tool suffix, e.g. "rotated" → "photo (rotated).png".</param>
    /// <param name="write">Writes the output into the given stream.</param>
    /// <param name="validate">Returns null when the temp file is valid, otherwise an error message.</param>
    public static string WriteBeside(string source, string? newExtension, string? suffix,
        Action<Stream> write, Func<string, string?> validate)
    {
        string dir = Path.GetDirectoryName(source) ?? throw new IOException("Source has no directory");
        string ext = newExtension ?? Path.GetExtension(source);
        string baseName = Path.GetFileNameWithoutExtension(source) + (suffix is null ? "" : $" ({suffix})");
        string temp = Path.Combine(dir, $"{TempPrefix}{Guid.NewGuid():N}.tmp");

        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                File.SetAttributes(temp, FileAttributes.Hidden);
                write(fs);
            }

            if (new FileInfo(temp).Length == 0) throw new IOException("Output is empty");
            if (validate(temp) is { } error) throw new IOException($"Output failed validation: {error}");

            for (int n = 1; n < 1000; n++)
            {
                string candidate = Path.Combine(dir, n == 1 ? $"{baseName}{ext}" : $"{baseName} ({n}){ext}");
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                try
                {
                    File.Move(temp, candidate, overwrite: false); // MoveFileEx without REPLACE_EXISTING: atomic, same volume
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    continue; // lost a race with another writer; try the next name
                }
                File.SetAttributes(candidate, File.GetAttributes(candidate) & ~FileAttributes.Hidden);
                return candidate;
            }
            throw new IOException("No free output name after 999 attempts");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort; logged by caller via leftover check */ }
        }
    }

    public static string Sha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    public static IEnumerable<string> LeftoverTemps(string dir) =>
        Directory.EnumerateFiles(dir, $"{TempPrefix}*.tmp");
}
