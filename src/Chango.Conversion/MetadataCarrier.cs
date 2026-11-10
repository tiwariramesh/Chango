using System.Diagnostics;
using System.Text;

namespace Chango.Conversion;

/// <summary>
/// Copies metadata from an original into a result with <b>ExifTool</b> (Phil Harvey; ships with Chango, in a folder
/// next to Chango.exe), where the engine that made the result couldn't carry it: pictures Windows decodes (HEIC, camera
/// RAW), TIFF results (libvips writes no EXIF there), and video or audio made by Windows' transcoder. Camera, date
/// taken, GPS location, author, title, keywords: every tag the result's format can hold. The orientation is not
/// copied, because results are already upright.
/// <para>Best effort by design: a result is never failed or delayed for long because its metadata couldn't be
/// copied. One ExifTool process stays open for the whole session (<c>-stay_open</c>), so each copy costs a few
/// tens of milliseconds instead of a process start.</para>
/// </summary>
public static class MetadataCarrier
{
    private static readonly AppFinder ExifTool = new(() => ExternalTools.Bundled(@"exiftool\exiftool.exe")
        ?? ExternalTools.Find("exiftool.exe", null, @"scoop\shims\exiftool.exe"));

    private static readonly object Gate = new();
    private static Process? _process;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The exiftool.exe in use, or null when there is none.</summary>
    public static string? ExecutablePath => ExifTool.Value;
    public static bool Available => ExecutablePath is not null;

    /// <summary>Formats ExifTool writes and that hold tags worth having; for others there's nothing to do.</summary>
    private static readonly HashSet<string> Writable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".tif", ".tiff", ".heic", ".avif",
        ".mp4", ".m4v", ".mov", ".m4a", ".3gp", ".mp3", ".flac", ".pdf",
    };

    public static bool CanWrite(string path) => Writable.Contains(Path.GetExtension(path));

    /// <summary>
    /// Copies every tag of <paramref name="source"/> that <paramref name="target"/>'s format can hold into it, in
    /// place. The target must have its real extension (ExifTool chooses the writer by it) and must not be hidden.
    /// Returns false (and leaves the target as it was) when it couldn't.
    /// </summary>
    public static bool CopyAll(string source, string target)
    {
        if (!CanWrite(target) || !File.Exists(source) || ExecutablePath is not { } exe) return false;
        string[] args =
        [
            "-charset", "filename=utf8", "-tagsFromFile", source, "-all:all", "--Orientation",
            "-overwrite_original", "-m", "-q", "-q", target,
        ];
        lock (Gate)
        {
            try
            {
                return Execute(exe, args);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                Stop();
                return false;
            }
        }
    }

    private static bool Execute(string exe, string[] args)
    {
        var process = _process is { HasExited: false } p ? p : (_process = Start(exe));
        var input = process.StandardInput;
        foreach (var a in args) input.Write(a + "\n");
        input.Write("-echo3\n{status ${status}}\n-execute\n"); // ExifTool reports its own status line before {ready}
        input.Flush();

        var read = Task.Run(() =>
        {
            bool ok = true;
            while (process.StandardOutput.ReadLine() is { } line)
            {
                // "{status 1}" or "{status 2}": the copy failed; anything else (0, or no status support) counts as done.
                if (line.StartsWith("{status ", StringComparison.Ordinal)) ok = !(line.Length > 8 && line[8] is >= '1' and <= '9');
                if (line.StartsWith("{ready", StringComparison.Ordinal)) return ok;
            }
            throw new IOException("ExifTool stopped.");
        });
        if (!read.Wait(Timeout)) throw new TimeoutException();
        return read.Result;
    }

    private static Process Start(string exe)
    {
        var start = new ProcessStartInfo(exe)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-stay_open", "True", "-@", "-" }) start.ArgumentList.Add(a);
        var process = Process.Start(start) ?? throw new IOException("ExifTool didn't start.");
        process.ErrorDataReceived += (_, _) => { }; // drained: a full error pipe would stall it
        process.BeginErrorReadLine();
        AppDomain.CurrentDomain.ProcessExit -= OnExit;
        AppDomain.CurrentDomain.ProcessExit += OnExit;
        return process;
    }

    private static void OnExit(object? sender, EventArgs e)
    {
        lock (Gate) Stop();
    }

    /// <summary>Ends the ExifTool process (politely, then firmly).</summary>
    private static void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Write("-stay_open\nFalse\n");
                process.StandardInput.Flush();
                if (!process.WaitForExit(2000)) process.Kill(entireProcessTree: true);
            }
        }
        catch { /* already gone */ }
        process.Dispose();
    }
}
