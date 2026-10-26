using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Chango.Windows.Shell;

/// <summary>
/// "Chango" in File Explorer's right-click menu. The entry itself is ChangoShell.dll (src/Chango.ShellMenu):
/// choosing it sends the selected paths to Chango's message window (see <see cref="TryReadFiles"/>).
/// The Microsoft Store package registers the DLL in its manifest (Windows 11's main menu); the GitHub version
/// registers it here, for the current user only (the classic menu, under "Show more options" on Windows 11).
/// </summary>
public static class ContextMenu
{
    public const string Clsid = "{29BADCB7-5BF2-4D63-84F8-67D64C5194F5}";
    /// <summary>The Store package's name for the DLL (its manifest names it). The GitHub version uses <see cref="VersionedDll"/>.</summary>
    public const string DllName = "ChangoShell.dll";

    /// <summary>
    /// "ChangoShell-0.4.4.dll": the GitHub version gives the DLL a new name every release. File Explorer keeps the
    /// DLL it loaded open, and a per-user installer can't replace an open file, so a fixed name would block updates.
    /// </summary>
    public static string VersionedDll(string version) => $"ChangoShell-{version}.dll";
    /// <summary>COPYDATASTRUCT.dwData of an "open these files" message from the DLL.</summary>
    public const int OpenFilesMessage = 0x0D0F;
    private const int WM_COPYDATA = 0x004A, MaxItems = 500;

    private const string VerbKey = @"Software\Classes\AllFilesystemObjects\shell\Chango";
    private const string ClsidKey = @"Software\Classes\CLSID\" + Clsid;
    private const string SettingsKey = @"Software\Chango";

    /// <summary>
    /// Applies the setting. Always records it where the DLL looks (so the Store version's entry hides itself when off);
    /// on the GitHub version, also adds or removes the registration. Returns false if registering failed.
    /// </summary>
    public static bool Apply(bool enabled, bool packaged, string appDirectory, string version)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey)) key.SetValue("ContextMenu", enabled ? 1 : 0, RegistryValueKind.DWord);
            if (packaged) return true;
            string dll = Path.Combine(appDirectory, VersionedDll(version));
            if (!File.Exists(dll)) dll = Path.Combine(appDirectory, DllName); // a build made without a version
            if (enabled && File.Exists(dll)) Register(dll);
            else Unregister();
            RemoveOldDlls(appDirectory, keep: dll);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"Right-click menu: {ex.Message}");
            return false;
        }
    }

    private static void Register(string dll)
    {
        using (var clsid = Registry.CurrentUser.CreateSubKey(ClsidKey))
        {
            clsid.SetValue("", "Chango right-click menu");
            using var server = clsid.CreateSubKey("InprocServer32");
            server.SetValue("", dll);
            server.SetValue("ThreadingModel", "Apartment");
        }
        using (var verb = Registry.CurrentUser.CreateSubKey(VerbKey))
        {
            verb.SetValue("ExplorerCommandHandler", Clsid);
            verb.SetValue("Icon", Path.Combine(Path.GetDirectoryName(dll)!, "Chango.exe") + ",0");
        }
        Notify();
    }

    /// <summary>Deletes the right-click DLLs of earlier versions; one File Explorer still has open goes next time.</summary>
    private static void RemoveOldDlls(string appDirectory, string keep)
    {
        foreach (var old in Directory.EnumerateFiles(appDirectory, "ChangoShell*.dll"))
        {
            if (string.Equals(old, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); } catch (IOException) { /* still loaded by File Explorer */ } catch (UnauthorizedAccessException) { }
        }
    }

    public static void Unregister()
    {
        bool had = Registry.CurrentUser.OpenSubKey(VerbKey) is { } k && Close(k);
        Registry.CurrentUser.DeleteSubKeyTree(VerbKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ClsidKey, throwOnMissingSubKey: false);
        if (had) Notify();
    }

    private static bool Close(RegistryKey key) { key.Dispose(); return true; }

    /// <summary>Tells Explorer the menus changed, so the entry appears or goes without a restart.</summary>
    private static void Notify() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// The paths in an "open these files" WM_COPYDATA from the DLL, checked: only existing files and folders, at most
    /// 500. Null for any other message.
    /// </summary>
    public static IReadOnlyList<string>? TryReadFiles(int msg, IntPtr lParam)
    {
        if (msg != WM_COPYDATA || lParam == IntPtr.Zero) return null;
        var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        if (data.dwData != OpenFilesMessage || data.lpData == IntPtr.Zero || data.cbData < 2 || data.cbData > 4 * 1024 * 1024) return null;
        string text = Marshal.PtrToStringUni(data.lpData, data.cbData / 2);
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length < 32_768 && Path.IsPathFullyQualified(p) && (File.Exists(p) || Directory.Exists(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxItems).ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
