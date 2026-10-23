using System.Formats.Tar;
using System.IO.Compression;
using Chango.Core;

namespace Chango.Tools;

/// <summary>
/// Zip anything (files and folders) and extract ZIP, TAR, TAR.GZ/TGZ and GZ, with .NET's built-in archive support.
/// 7Z, RAR, TAR.XZ, TAR.BZ2, CAB and ISO extract, and 7Z and TAR.GZ archives are made, with the tar that comes with
/// Windows (libarchive). Extraction goes into a new folder named after the archive; entries that would escape it are
/// rejected (libarchive refuses ".." and absolute paths by default).
/// </summary>
public sealed class ArchiveProvider : IOperationProvider
{
    public const string ProviderId = "archive";
    public string Id => ProviderId;

    public enum Kind { None, Zip, Tar, TarGz, Gz, Libarchive }

    /// <summary>Windows' own tar (bsdtar/libarchive), in Windows 10 1803 and later.</summary>
    private static string WindowsTar => Path.Combine(Environment.SystemDirectory, "tar.exe");
    private static readonly Lazy<bool> HasTar = new(() => File.Exists(WindowsTar)); // wheel queries must not touch the disk

    private static readonly string[] LibarchiveSuffixes = [".7z", ".rar", ".tar.xz", ".txz", ".tar.bz2", ".tbz2", ".tbz", ".cab", ".iso"];

    public static Kind KindOf(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz")) return Kind.TarGz;
        if (LibarchiveSuffixes.Any(name.EndsWith)) return HasTar.Value ? Kind.Libarchive : Kind.None;
        return Path.GetExtension(name) switch { ".zip" => Kind.Zip, ".tar" => Kind.Tar, ".gz" => Kind.Gz, _ => Kind.None };
    }

    private static readonly OperationGroup Archive = new("archive.create", "Archive", "");

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (files.IsEmpty) yield break;
        if (mode == OperationMode.Actions)
        {
            // Zip stays the default (a drop on the segment); 7Z and TAR.GZ are in its sub-wheel.
            bool more = HasTar.Value;
            var group = more ? Archive : null;
            yield return new("action.zip", ProviderId, OperationMode.Actions, "Zip", "", "Put in a ZIP", "", "Zipping", 235, Group: group, DoneTitle: "Zipped");
            if (more)
            {
                yield return new("action.archive.7z", ProviderId, OperationMode.Actions, "7Z", "", "Put in a 7Z archive", "", "Making a 7Z archive", 236, Group: group, DoneTitle: "Archived");
                yield return new("action.archive.tar.gz", ProviderId, OperationMode.Actions, "TAR.GZ", "", "Put in a TAR.GZ archive", "", "Making a TAR.GZ archive", 237, Group: group, DoneTitle: "Archived");
            }
        }
        else if (mode == OperationMode.Tools && files.All(f => KindOf(f) != Kind.None))
            yield return new("tool.archive.extract", ProviderId, OperationMode.Tools, "Extract", "", "Extract here", "", "Extracting", 100);
    }

    public Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct) => Task.Run(() =>
    {
        if (operation.Id is "action.zip" or "action.archive.7z" or "action.archive.tar.gz")
        {
            try
            {
                string output = operation.Id == "action.zip" ? Zip(files, context, ct) : Pack(files, operation.Id == "action.archive.7z" ? ".7z" : ".tar.gz", context, ct);
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
            try { progress.Report(new FileOutcome(file, OutcomeStatus.Succeeded, Extract(file, context, ct))); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { progress.Report(new FileOutcome(file, OutcomeStatus.Failed, Message: Friendly(ex))); }
        }
    }, ct);

    /// <summary>One item → "name.zip"; several → "Archive.zip", next to the first item.</summary>
    private static string Zip(FileSet items, OperationContext context, CancellationToken ct)
    {
        string first = Path.TrimEndingDirectorySeparator(items[0]);
        string baseName = items.Count == 1 ? Path.GetFileName(first) : "Archive";
        // SafeOutput names the result from a source path; give it one whose name (minus extension) is what we want.
        string nominal = Path.Combine(Path.GetDirectoryName(first)!, baseName + ".zip");
        return SafeOutput.Write(nominal, context.Output, ".zip", null, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                string trimmed = Path.TrimEndingDirectorySeparator(item);
                if (File.Exists(trimmed)) zip.CreateEntryFromFile(trimmed, Path.GetFileName(trimmed), CompressionLevel.Optimal);
                else if (Directory.Exists(trimmed)) AddFolder(zip, trimmed, Path.GetFileName(trimmed), ct);
                else throw new FileNotFoundException("An item no longer exists.", trimmed);
            }
        }, temp => ZipValidates(temp, items.Count), ct);
    }

    /// <summary>One item → "name.7z"; several → "Archive.7z", next to the first item. Made by Windows' tar.</summary>
    private static string Pack(FileSet items, string extension, OperationContext context, CancellationToken ct)
    {
        string first = Path.TrimEndingDirectorySeparator(items[0]);
        string baseName = items.Count == 1 ? Path.GetFileName(first) : "Archive";
        string nominal = Path.Combine(Path.GetDirectoryName(first)!, baseName + ".x");
        var args = new List<string> { "-c", extension == ".7z" ? "--format=7zip" : "-z", "-f" };
        return SafeOutput.WriteToPath(nominal, context.Output, extension, null, temp =>
        {
            args.Add(temp);
            foreach (var item in items)
            {
                string trimmed = Path.TrimEndingDirectorySeparator(item);
                if (!File.Exists(trimmed) && !Directory.Exists(trimmed)) throw new FileNotFoundException("An item no longer exists.", trimmed);
                args.AddRange(["-C", Path.GetDirectoryName(trimmed)!, Path.GetFileName(trimmed)]); // stored by name, not full path
            }
            RunTar(args, ct);
        }, _ => null, ct, hideWhileWriting: false, tempKeepsExtension: true); // tar can't replace a hidden file
    }

    private static void RunTar(IEnumerable<string> args, CancellationToken ct)
    {
        var start = new System.Diagnostics.ProcessStartInfo(WindowsTar) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("tar didn't start.");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        _ = process.StandardOutput.ReadToEndAsync(ct);
        using (ct.Register(() => { try { process.Kill(true); } catch { } })) process.WaitForExit();
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string detail = stderr.Result.Trim().Split('\n').LastOrDefault()?.Replace("tar.exe: ", "").Trim() ?? "";
            throw new TarException(detail.Length > 0 ? detail : "The archive is damaged or isn't a format Chango can open.");
        }
    }

    /// <summary>Windows' tar refused: its own message says why ("Truncated input file", "Unrecognized archive format" …).</summary>
    private sealed class TarException(string message) : Exception(message);

    private static void AddFolder(ZipArchive zip, string folder, string prefix, CancellationToken ct)
    {
        bool any = false;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            zip.CreateEntryFromFile(file, $"{prefix}/{Path.GetFileName(file)}", CompressionLevel.Optimal);
            any = true;
        }
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            AddFolder(zip, dir, $"{prefix}/{Path.GetFileName(dir)}", ct);
            any = true;
        }
        if (!any) zip.CreateEntry(prefix + "/"); // keep empty folders
    }

    private static string? ZipValidates(string path, int minimumEntries)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Count >= Math.Min(1, minimumEntries) ? null : "the ZIP is empty";
    }

    private static string Extract(string archive, OperationContext context, CancellationToken ct)
    {
        var kind = KindOf(archive);
        string name = Path.GetFileName(archive);
        string stem = kind switch
        {
            Kind.TarGz when name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) => name[..^4],
            Kind.TarGz => name[..^7],
            Kind.Libarchive when name.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase)
                => name[..name.LastIndexOf(".tar", StringComparison.OrdinalIgnoreCase)],
            _ => Path.GetFileNameWithoutExtension(name),
        };

        if (kind == Kind.Gz)
        {
            // A single compressed file: "data.csv.gz" → "data.csv".
            string nominal = Path.Combine(Path.GetDirectoryName(archive)!, stem);
            return SafeOutput.Write(nominal, context.Output, Path.GetExtension(stem), null, output =>
            {
                using var input = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
                input.CopyTo(output);
            }, _ => null, ct);
        }

        return SafeOutput.WriteFolder(archive, context.Output, stem, folder =>
        {
            switch (kind)
            {
                case Kind.Zip:
                    ZipFile.ExtractToDirectory(archive, folder, overwriteFiles: false); // rejects entries outside the folder
                    break;
                case Kind.Tar:
                    TarFile.ExtractToDirectory(archive, folder, overwriteFiles: false);
                    break;
                case Kind.TarGz:
                {
                    using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(gz, folder, overwriteFiles: false);
                    break;
                }
                case Kind.Libarchive:
                    RunTar(["-x", "-k", "-f", archive, "-C", folder], ct); // -k: never overwrite
                    break;
            }
        }, ct);
    }

    private static string Friendly(Exception ex) => ex switch
    {
        TarException => ex.Message,
        InvalidDataException => "The archive is damaged or isn't a format Chango can open.",
        IOException io when io.Message.Contains("outside", StringComparison.OrdinalIgnoreCase) =>
            "The archive tries to write outside its folder, so it wasn't extracted.",
        FileNotFoundException => "An item no longer exists.",
        UnauthorizedAccessException => "Windows denied access (a file may be in use or protected).",
        IOException io when io.HResult == unchecked((int)0x80070070) => "There isn't enough disk space.",
        _ => ex.Message,
    };

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Archives", HasTar.Value
            ? "Zip, 7Z or TAR.GZ any files or folders · extract ZIP, 7Z, RAR, TAR, TAR.GZ, TAR.XZ, TAR.BZ2, GZ, CAB, ISO"
            : "Zip any files or folders · extract ZIP, TAR, TAR.GZ/TGZ, GZ");
    }
}
