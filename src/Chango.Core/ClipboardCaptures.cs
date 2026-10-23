namespace Chango.Core;

/// <summary>
/// Images taken from the clipboard (Ctrl+Alt+Space with nothing selected) are saved here first, so every option works
/// on them like on any file. Results never land here: they go to the folder in front, or Pictures.
/// </summary>
public static class ClipboardCaptures
{
    /// <summary>Set once by the app (under its local data folder); null in tests and tools.</summary>
    public static string? Folder { get; set; }

    public static bool IsCapture(string path) =>
        Folder is { } folder && Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
