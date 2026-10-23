using System.Formats.Tar;
using System.IO.Compression;
using OrbitDrop.Core;

namespace OrbitDrop.Tools;

/// <summary>
/// Zip anything (files and folders) and extract ZIP, TAR, TAR.GZ/TGZ and GZ, with .NET's built-in archive support.
/// Extraction goes into a new folder named after the archive; entries that would escape it are rejected.
/// </summary>
public sealed class ArchiveProvider : IOperationProvider
{
    public const string ProviderId = "archive";
    public string Id => ProviderId;

    public enum Kind { None, Zip, Tar, TarGz, Gz }

    public static Kind KindOf(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz")) return Kind.TarGz;
        return Path.GetExtension(name) switch { ".zip" => Kind.Zip, ".tar" => Kind.Tar, ".gz" => Kind.Gz, _ => Kind.None };
    }

    public IEnumerable<OperationDescriptor> GetOperations(FileSet files, OperationMode mode)
    {
        if (files.IsEmpty) yield break;
        if (mode == OperationMode.Actions)
            yield return new("action.zip", ProviderId, OperationMode.Actions, "Zip", "", "Put in a ZIP", "", "Zipping", 235, DoneTitle: "Zipped");
        else if (mode == OperationMode.Tools && files.All(f => KindOf(f) != Kind.None))
            yield return new("tool.archive.extract", ProviderId, OperationMode.Tools, "Extract", "", "Extract here", "", "Extracting", 100);
    }

    public Task ExecuteAsync(OperationDescriptor operation, FileSet files, OperationContext context,
        IProgress<FileOutcome> progress, CancellationToken ct) => Task.Run(() =>
    {
        if (operation.Id == "action.zip")
        {
            try
            {
                string output = Zip(files, context, ct);
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
            }
        }, ct);
    }

    private static string Friendly(Exception ex) => ex switch
    {
        InvalidDataException => "The archive is damaged or isn't a format OrbitDrop can open.",
        IOException io when io.Message.Contains("outside", StringComparison.OrdinalIgnoreCase) =>
            "The archive tries to write outside its folder, so it wasn't extracted.",
        FileNotFoundException => "An item no longer exists.",
        UnauthorizedAccessException => "Windows denied access (a file may be in use or protected).",
        IOException io when io.HResult == unchecked((int)0x80070070) => "There isn't enough disk space.",
        _ => ex.Message,
    };

    public IEnumerable<(string Category, string Detail)> DescribeCapabilities()
    {
        yield return ("Archives", "Zip any files or folders · extract ZIP, TAR, TAR.GZ/TGZ, GZ");
    }
}
