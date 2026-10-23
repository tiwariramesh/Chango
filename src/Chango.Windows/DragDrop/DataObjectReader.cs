using System.Runtime.InteropServices;
using Chango.Windows.Interop;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Chango.Windows.DragDrop;

/// <summary>Reads file paths and format lists from a (cross-process, marshaled) OLE data object.</summary>
public static class DataObjectReader
{
    private const short CF_HDROP = 15;
    private static readonly short CfDropDescription = (short)Win32.RegisterClipboardFormat("DropDescription");

    private static readonly Dictionary<int, string> PredefinedFormats = new()
    {
        [1] = "CF_TEXT", [2] = "CF_BITMAP", [7] = "CF_OEMTEXT", [8] = "CF_DIB", [13] = "CF_UNICODETEXT",
        [15] = "CF_HDROP", [16] = "CF_LOCALE", [17] = "CF_DIBV5",
    };

    private const short CF_UNICODETEXT = 13;
    private static readonly short CfChromiumCustom = (short)Win32.RegisterClipboardFormat("Chromium Web Custom MIME Data Format");
    private static readonly short CfUrlW = (short)Win32.RegisterClipboardFormat("UniformResourceLocatorW");
    private static readonly short CfFileNameW = (short)Win32.RegisterClipboardFormat("FileNameW");

    /// <summary>
    /// The files being dragged. Explorer and most apps provide CF_HDROP. Apps that don't (VS Code's Explorer, and
    /// other Chromium/Electron apps) still describe the files: as Chromium custom web data, plain text with one full
    /// path per line, a file:// URL or a single file name. Apart from CF_HDROP, only paths that exist are returned.
    /// </summary>
    public static string[] ReadFileDrop(ComIDataObject data)
    {
        // The real paths come first. VS Code's Explorer sends each item's full path as text (files and folders) and
        // file:// links in "ResourceURLs" (files only); its other views send "CodeFiles". Its CF_HDROP is a promise
        // of a temporary download copy (DownloadURL) and can fail to render (DV_E_FORMATETC), so it comes after them.
        // Plain text only counts when every line is an existing full path. Each source is tried on its own, so one
        // that fails never hides the others.
        var sources = new (string Name, Func<ComIDataObject, IEnumerable<string>> Read)[]
        {
            ("CodeFiles", d => FromChromiumCustom(d, "CodeFiles")), ("text", FromText),
            ("ResourceURLs", d => FromChromiumCustom(d, "ResourceURLs")), ("CF_HDROP", ReadHDrop), ("URL", FromUrl),
            ("FileNameW", FromFileName), ("text/uri-list", d => FromChromiumCustom(d, "text/uri-list")),
        };
        foreach (var (name, read) in sources)
        {
            string[] found;
            // CF_HDROP is used as given (as before); the other sources only count paths that exist.
            try { found = name == "CF_HDROP" ? read(data).ToArray() : Existing(read(data)); }
            catch (Exception ex) { Diagnostics.Write($"Drag data: {name} unreadable ({ex.Message})"); continue; }
            if (found.Length == 0) continue;
            if (name != "CF_HDROP") Diagnostics.Write($"Drag data: {found.Length} file(s) from {name}");
            return found;
        }
        return [];
    }

    /// <summary>One entry of Chromium's custom web data: "CodeFiles" (JSON paths), "ResourceURLs" (JSON URIs) or "text/uri-list".</summary>
    private static IEnumerable<string> FromChromiumCustom(ComIDataObject data, string type)
    {
        if (ReadBytes(data, CfChromiumCustom) is not { } bytes) return [];
        foreach (var (t, value) in ParseChromiumCustomData(bytes))
        {
            if (!t.Equals(type, StringComparison.OrdinalIgnoreCase)) continue; // Blink lowercases the types
            return type switch
            {
                "CodeFiles" => JsonStrings(value),
                "ResourceURLs" => PathsFromUris(JsonStrings(value)),
                _ => PathsFromUris(value.Split('\n')),
            };
        }
        return [];
    }

    private static IEnumerable<string> FromUrl(ComIDataObject data) =>
        ReadBytes(data, CfUrlW) is { } b ? PathsFromUris(Encoding.Unicode.GetString(b).TrimEnd('\0').Split('\n')) : [];

    private static IEnumerable<string> FromFileName(ComIDataObject data) =>
        ReadBytes(data, CfFileNameW) is { } b ? [Encoding.Unicode.GetString(b).TrimEnd('\0')] : [];

    private static IEnumerable<string> FromText(ComIDataObject data) =>
        ReadBytes(data, CF_UNICODETEXT) is { } b ? PathsFromText(Encoding.Unicode.GetString(b).TrimEnd('\0'), p => File.Exists(p) || Directory.Exists(p)) : [];

    /// <summary>
    /// Chromium's custom web data (a base::Pickle): uint32 payload size, uint32 entry count, then for each entry two
    /// UTF-16 strings (type, data), each an int32 length in chars followed by the text padded to 4 bytes.
    /// </summary>
    public static List<(string Type, string Value)> ParseChromiumCustomData(byte[] bytes)
    {
        var result = new List<(string, string)>();
        int pos = 4; // the pickle header: payload size
        if (bytes.Length < 8) return result;
        uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
        string? Next()
        {
            if (pos + 4 > bytes.Length) return null;
            int chars = BitConverter.ToInt32(bytes, pos); pos += 4;
            if (chars < 0 || pos + chars * 2 > bytes.Length) return null;
            string s = Encoding.Unicode.GetString(bytes, pos, chars * 2);
            pos += (chars * 2 + 3) & ~3;
            return s;
        }
        for (uint i = 0; i < count && i < 256; i++)
        {
            if (Next() is not { } type || Next() is not { } value) break;
            result.Add((type, value));
        }
        return result;
    }

    /// <summary>file:// (and VS Code's vscode-file://vscode-app/...) URIs, one per entry, as local paths. Blank lines and "#" comments are ignored.</summary>
    public static string[] PathsFromUris(IEnumerable<string> uris) =>
        uris.Select(u => u.Trim().Trim('\0'))
            .Where(u => u.Length > 0 && !u.StartsWith('#'))
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.IsFile || uri.Scheme == "vscode-file") ? LocalPath(uri) : null)
            .OfType<string>().ToArray();

    /// <summary>VS Code writes the drive colon encoded ("file:///c%3A/..."), which Uri.LocalPath leaves as "/c:/...".</summary>
    private static string LocalPath(Uri uri)
    {
        if (uri.IsFile && uri.IsUnc) return uri.LocalPath;
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':') path = path[1..];
        return path.Replace('/', '\\');
    }

    /// <summary>Plain text counts as a file list only if every non-empty line is a full path that exists.</summary>
    public static string[] PathsFromText(string text, Func<string, bool> exists)
    {
        var lines = text.Split('\n').Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0).ToArray();
        if (lines.Length == 0 || lines.Length > 1000) return [];
        return lines.All(l => Path.IsPathFullyQualified(l) && exists(l)) ? lines : [];
    }

    private static string[] JsonStrings(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    private static string[] Existing(IEnumerable<string> paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p))
             .Select(p => p.TrimEnd('\\', '/') is { Length: > 2 } t ? t : p) // "C:\" stays as is
             .Where(p => File.Exists(p) || Directory.Exists(p))
             .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static byte[]? ReadBytes(ComIDataObject data, short cf)
    {
        var fmt = HGlobalFormat(cf);
        if (cf == 0 || data.QueryGetData(ref fmt) != Ole.S_OK) return null;
        data.GetData(ref fmt, out STGMEDIUM medium);
        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero) return null;
            int size = (int)Math.Min((ulong)Win32.GlobalSize(medium.unionmember), 16UL * 1024 * 1024);
            IntPtr ptr = Win32.GlobalLock(medium.unionmember);
            if (ptr == IntPtr.Zero) return null;
            try { var bytes = new byte[size]; Marshal.Copy(ptr, bytes, 0, size); return bytes; }
            finally { Win32.GlobalUnlock(medium.unionmember); }
        }
        finally { Win32.ReleaseStgMedium(ref medium); }
    }

    private static string[] ReadHDrop(ComIDataObject data)
    {
        var fmt = HGlobalFormat(CF_HDROP);
        if (data.QueryGetData(ref fmt) != Ole.S_OK) return [];

        data.GetData(ref fmt, out STGMEDIUM medium);
        try
        {
            IntPtr hDrop = medium.unionmember;
            uint count = Win32.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var files = new string[count];
            for (uint i = 0; i < count; i++)
            {
                uint len = Win32.DragQueryFile(hDrop, i, null, 0);
                var sb = new StringBuilder((int)len + 1);
                Win32.DragQueryFile(hDrop, i, sb, len + 1);
                files[i] = sb.ToString();
            }
            return files;
        }
        finally
        {
            Win32.ReleaseStgMedium(ref medium);
        }
    }

    public static List<string> ListFormats(ComIDataObject data)
    {
        var names = new List<string>();
        try
        {
            IEnumFORMATETC e = data.EnumFormatEtc(DATADIR.DATADIR_GET);
            var one = new FORMATETC[1];
            var fetched = new int[1];
            while (e.Next(1, one, fetched) == Ole.S_OK && fetched[0] == 1)
                names.Add(FormatName((ushort)one[0].cfFormat));
        }
        catch (Exception ex)
        {
            names.Add($"<EnumFormatEtc failed: {ex.Message}>");
        }
        return names;
    }

    /// <summary>
    /// Sets the Windows drag tooltip ("Convert to PNG") shown next to the shell drag image.
    /// Requires the source to use IDragSourceHelper2 (Explorer does) and the target to use IDropTargetHelper.
    /// </summary>
    public static bool TrySetDropDescription(ComIDataObject data, int imageType, string message, string insert)
    {
        var desc = new Ole.DROPDESCRIPTION { type = imageType, szMessage = message, szInsert = insert };
        int size = Marshal.SizeOf<Ole.DROPDESCRIPTION>();
        IntPtr hGlobal = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE | Win32.GMEM_ZEROINIT, (UIntPtr)size);
        if (hGlobal == IntPtr.Zero) return false;

        IntPtr ptr = Win32.GlobalLock(hGlobal);
        Marshal.StructureToPtr(desc, ptr, false);
        Win32.GlobalUnlock(hGlobal);

        var fmt = HGlobalFormat(CfDropDescription);
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = hGlobal, pUnkForRelease = null };
        try
        {
            data.SetData(ref fmt, ref medium, true); // ownership transfers on success
            return true;
        }
        catch
        {
            Win32.GlobalFree(hGlobal);
            return false;
        }
    }

    private static FORMATETC HGlobalFormat(short cf) => new()
    {
        cfFormat = cf, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, ptd = IntPtr.Zero, tymed = TYMED.TYMED_HGLOBAL,
    };

    private static string FormatName(ushort cf)
    {
        if (PredefinedFormats.TryGetValue(cf, out var name)) return name;
        var sb = new StringBuilder(128);
        return Win32.GetClipboardFormatName(cf, sb, sb.Capacity) > 0 ? sb.ToString() : $"#{cf}";
    }
}
