namespace Flipr.Infrastructure;

/// <summary>
/// Minimal local file log (daily files, 7 kept). Never uploaded anywhere.
/// File names and paths are only written when <see cref="Detailed"/> is on, via <see cref="Path"/>.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _directory;

    /// <summary>Where the logs go (null before <see cref="Initialize"/>).</summary>
    public static string? Folder => _directory;
    private static StreamWriter? _writer;
    private static DateOnly _day;

    public static bool Detailed { get; set; }

    public static void Initialize(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        foreach (var old in new DirectoryInfo(directory).GetFiles("flipr-*.log")
                     .OrderByDescending(f => f.Name).Skip(7))
        {
            try { old.Delete(); } catch { /* in use or gone */ }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    /// <summary>Renders a path for the log: the full path in detailed mode, otherwise only the extension.</summary>
    public static string Path(string path) => Detailed ? path : $"<{System.IO.Path.GetExtension(path)} file>";

    private static void Write(string level, string message)
    {
        if (_directory is null) return;
        lock (Gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (_writer is null || today != _day)
                {
                    _writer?.Dispose();
                    _day = today;
                    _writer = new StreamWriter(System.IO.Path.Combine(_directory, $"flipr-{today:yyyyMMdd}.log"), append: true) { AutoFlush = true };
                }
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {level,-5} {message}");
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }

    public static void Shutdown()
    {
        lock (Gate) { _writer?.Dispose(); _writer = null; }
    }
}
