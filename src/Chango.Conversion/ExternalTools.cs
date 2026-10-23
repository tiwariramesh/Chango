using System.Diagnostics;
using Microsoft.Win32;

namespace Chango.Conversion;

/// <summary>
/// Finds an app and remembers it. While it isn't there, looks again every 15 seconds, so an app installed while
/// Chango is running is picked up without a restart.
/// </summary>
internal sealed class AppFinder(Func<string?> find)
{
    private readonly object _gate = new();
    private string? _path;
    private bool _searched;
    private long _searchedAt;

    public string? Value
    {
        get
        {
            lock (_gate)
            {
                long now = Environment.TickCount64;
                bool stale = !_searched || (_path is null ? now - _searchedAt > 15_000 : !File.Exists(_path));
                if (stale) { _path = find(); _searched = true; _searchedAt = now; }
                return _path;
            }
        }
    }
}

/// <summary>
/// Command-line tools Chango uses when they're installed (LibreOffice, FFmpeg) or that come with Windows (tar):
/// finding them, and running them invisibly with a time limit.
/// </summary>
internal static class ExternalTools
{
    /// <summary>
    /// App Paths, Program Files, per-user installs, the user-profile path (scoop …), the Microsoft Store and winget
    /// aliases, any <paramref name="extra"/> candidates and PATH. Prefers the console launcher (.com), which waits for the result.
    /// </summary>
    public static string? Find(string exe, string? programFilesPath, string? homePath, params string?[] extra)
    {
        var candidates = new List<string?>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
                candidates.Add(key?.GetValue(null) as string);
            }
            catch { /* no access */ }
        }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (programFilesPath is not null)
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), programFilesPath));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), programFilesPath));
            candidates.Add(Path.Combine(local, "Programs", programFilesPath));                                     // per-user installer
        }
        if (homePath is not null)
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), homePath)); // scoop
        candidates.Add(Path.Combine(local, "Microsoft", "WindowsApps", exe));                                 // Microsoft Store alias
        candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", exe));                             // winget alias
        candidates.AddRange(extra);
        // PATH as this process got it, then as it is now: a tool installed after Chango started is still found.
        string searchPath = string.Join(';', Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        foreach (var dir in searchPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim('"'), exe)); } catch (ArgumentException) { /* a malformed PATH entry */ }
        }
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            string path = c.Trim('"');
            string console = Path.ChangeExtension(path, ".com");
            if (File.Exists(console)) return console;
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// Runs <paramref name="exe"/> without a window; throws with the tool's own first error line when it fails.
    /// <paramref name="onOutput"/> and <paramref name="onError"/> see each line as it arrives (for progress).
    /// </summary>
    public static async Task RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct,
        Action<string>? onOutput = null, Action<string>? onError = null, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(exe)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new IOException($"{Path.GetFileName(exe)} didn't start.");
        // Keep only the last lines of each stream: enough for an error message, bounded for long runs.
        var stdout = new Queue<string>();
        var stderr = new Queue<string>();
        var reading = Task.WhenAll(Pump(process.StandardOutput, stdout, onOutput), Pump(process.StandardError, stderr, onError));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException();
        }
        await reading;
        if (process.ExitCode != 0)
        {
            string detail = LastMeaningful(stderr) ?? LastMeaningful(stdout) ?? "";
            throw new IOException($"{Path.GetFileNameWithoutExtension(exe)} couldn't convert it{(detail.Length > 0 ? ": " + detail : ".")}");
        }
    }

    private static async Task Pump(StreamReader reader, Queue<string> tail, Action<string>? onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            onLine?.Invoke(line);
            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > 20) tail.Dequeue();
            }
        }
    }

    private static string? LastMeaningful(Queue<string> lines)
    {
        lock (lines) return lines.Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
    }
}
