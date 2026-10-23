using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace OrbitProbe.Native;

/// <summary>Reads file paths and format lists from a (cross-process, marshaled) OLE data object.</summary>
internal static class DataObjectReader
{
    private const short CF_HDROP = 15;
    private static readonly short CfDropDescription = (short)Win32.RegisterClipboardFormat("DropDescription");

    private static readonly Dictionary<int, string> PredefinedFormats = new()
    {
        [1] = "CF_TEXT", [2] = "CF_BITMAP", [7] = "CF_OEMTEXT", [8] = "CF_DIB", [13] = "CF_UNICODETEXT",
        [15] = "CF_HDROP", [16] = "CF_LOCALE", [17] = "CF_DIBV5",
    };

    public static string[] ReadFileDrop(ComIDataObject data)
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
