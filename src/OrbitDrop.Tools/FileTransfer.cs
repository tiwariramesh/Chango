using System.Runtime.InteropServices;
using OrbitDrop.Core;

namespace OrbitDrop.Tools;

/// <summary>
/// Copy and move for files and folders that never overwrite: a taken name becomes "name (2)". Copies are written
/// to a hidden temp name in the destination and renamed into place, so a half-finished copy is never visible.
/// </summary>
public static class FileTransfer
{
    /// <summary>Copies <paramref name="source"/> (file or folder) into <paramref name="folder"/>; returns the new path.</summary>
    public static string CopyInto(string source, string folder, CancellationToken ct)
    {
        if (File.Exists(source))
            return SafeOutput.WriteToPath(source, new OutputOptions(OutputLocation.Folder, folder), null, null,
                writeToTemp: temp => File.Copy(source, temp, overwrite: true), // CopyFile can't replace a hidden file: hide afterwards
                validate: temp => new FileInfo(temp).Length == new FileInfo(source).Length ? null : "the copy is incomplete", ct,
                hideWhileWriting: false);

        if (!Directory.Exists(source)) throw new FileNotFoundException("The item no longer exists.", source);
        string temp = Path.Combine(folder, $"{SafeOutput.TempPrefix}{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(source, temp, ct);
            string target = FreeName(folder, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)), "");
            Directory.Move(temp, target);
            return target;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Moves <paramref name="source"/> (file or folder) into <paramref name="folder"/>; returns the new path.</summary>
    public static string MoveInto(string source, string folder, CancellationToken ct)
    {
        bool isFile = File.Exists(source);
        if (!isFile && !Directory.Exists(source)) throw new FileNotFoundException("The item no longer exists.", source);
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        string target = isFile ? FreeName(folder, Path.GetFileNameWithoutExtension(source), Path.GetExtension(source)) : FreeName(folder, name, "");

        if (isFile)
        {
            File.Move(source, target, overwrite: false); // same drive: a rename; other drive: copy then delete
            return target;
        }
        if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, target);
            return target;
        }
        // Folders can't be renamed across drives: copy (safely), then remove the original only once the copy exists.
        string copy = CopyInto(source, folder, ct);
        Directory.Delete(source, recursive: true);
        return copy;
    }

    /// <summary>"name.ext", or "name (2).ext", "name (3).ext" … whichever is free in <paramref name="folder"/>.</summary>
    public static string FreeName(string folder, string baseName, string extension)
    {
        for (int n = 1; n < 1000; n++)
        {
            string candidate = Path.Combine(folder, n == 1 ? $"{baseName}{extension}" : $"{baseName} ({n}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("No free name was found in the destination folder.");
    }

    private static void CopyDirectory(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            // Junctions and symbolic links are copied as links' targets would loop; skip them.
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)), ct);
        }
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> itself or inside it.</summary>
    public static bool IsWithin(string path, string folder)
    {
        string p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        string f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Common folders for the Copy to / Move to wheels.</summary>
public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static IReadOnlyList<(string Name, string Path)> Common()
    {
        var list = new List<(string, string)>
        {
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        };
        if (Downloads() is { } d) list.Add(("Downloads", d));
        return list.Where(f => Directory.Exists(f.Item2)).ToList();
    }

    public static string? Downloads()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }
        catch { return null; }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
