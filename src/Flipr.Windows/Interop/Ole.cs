using System.Runtime.InteropServices;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Flipr.Windows.Interop;

/// <summary>Classic OLE drag-and-drop COM contracts (ole32 / shell32).</summary>
public static class Ole
{
    public const int S_OK = 0;
    public const int DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;

    // grfKeyState bits
    public const int MK_LBUTTON = 0x1, MK_SHIFT = 0x4, MK_CONTROL = 0x8, MK_ALT = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL { public int X, Y; }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter([In, MarshalAs(UnmanagedType.Interface)] ComIDataObject pDataObj, int grfKeyState, POINTL pt, ref int pdwEffect);
        [PreserveSig] int DragOver(int grfKeyState, POINTL pt, ref int pdwEffect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([In, MarshalAs(UnmanagedType.Interface)] ComIDataObject pDataObj, int grfKeyState, POINTL pt, ref int pdwEffect);
    }

    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTargetHelper
    {
        [PreserveSig] int DragEnter(IntPtr hwndTarget, [In, MarshalAs(UnmanagedType.Interface)] ComIDataObject pDataObject, ref Win32.POINT ppt, int dwEffect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int DragOver(ref Win32.POINT ppt, int dwEffect);
        [PreserveSig] int Drop([In, MarshalAs(UnmanagedType.Interface)] ComIDataObject pDataObject, ref Win32.POINT ppt, int dwEffect);
        [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
    }

    public static readonly Guid CLSID_DragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");

    public static IDropTargetHelper? CreateDropTargetHelper()
    {
        var type = Type.GetTypeFromCLSID(CLSID_DragDropHelper);
        return type is null ? null : Activator.CreateInstance(type) as IDropTargetHelper;
    }

    // DROPDESCRIPTION (shlobj_core.h). DROPIMAGETYPE values:
    public const int DROPIMAGE_INVALID = -1, DROPIMAGE_NONE = 0, DROPIMAGE_COPY = 1, DROPIMAGE_LABEL = 6;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DROPDESCRIPTION
    {
        public int type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szMessage;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szInsert;
    }
}
