using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace OrbitProbe;

/// <summary>Thread-safe log: timestamped lines to a file and to a queue the UI drains.</summary>
public static class ProbeLog
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object FileLock = new();
    private static readonly ConcurrentQueue<string> Pending = new();
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    public static void Open()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitProbe");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
    }

    public static double NowMs => Clock.Elapsed.TotalMilliseconds;

    public static void Info(string message) => Write("INFO", message);
    public static void Signal(string message) => Write("SIG ", message);
    public static void Ole(string message) => Write("OLE ", message);
    public static void Pass(string message) => Write("PASS", message);
    public static void Fail(string message) => Write("FAIL", message);
    public static void Warn(string message) => Write("WARN", message);

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{NowMs,10:F1}ms] {level} {message}";
        Pending.Enqueue(line);
        lock (FileLock) _writer?.WriteLine(line);
    }

    public static IEnumerable<string> Drain()
    {
        while (Pending.TryDequeue(out var line)) yield return line;
    }

    public static void Close()
    {
        lock (FileLock) { _writer?.Dispose(); _writer = null; }
    }
}
