using System.Runtime.InteropServices;
using OrbitDrop.Windows.Interop;

namespace OrbitDrop.Windows.Shell;

/// <summary>
/// Notification-area icon via Shell_NotifyIcon (NOTIFYICON_VERSION_4). Re-adds itself when Explorer restarts.
/// The menu is drawn by the UI layer, so it matches the OrbitDrop design system.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 0x4D; // WM_APP + 'M'
    private readonly MessageWindow _window;
    private readonly uint _taskbarCreated;
    private readonly byte[] _icoBytes;
    private IntPtr _hIcon;
    private string _tooltip;
    private bool _added;

    public TrayIcon(MessageWindow window, byte[] icoBytes, string tooltip)
    {
        _window = window;
        _icoBytes = icoBytes;
        _tooltip = tooltip;
        _taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
        _window.Message += OnMessage;
    }

    /// <summary>Primary click (left click or keyboard select). Args: screen point in physical pixels.</summary>
    public event Action<Win32.POINT>? Invoked;
    /// <summary>Context menu request (right click, Shift+F10, menu key).</summary>
    public event Action<Win32.POINT>? ContextMenuRequested;

    public void Show()
    {
        _hIcon = CreateIcon(_icoBytes, Win32.GetSystemMetrics(Win32.SM_CXSMICON));
        var data = Data(Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP);
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
        data.uVersion = Win32.NOTIFYICON_VERSION_4;
        Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
        Diagnostics.Write(_added ? "Tray icon added" : "Tray icon could not be added (will retry when the taskbar restarts)");
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        if (!_added) return;
        var data = Data(Win32.NIF_TIP | Win32.NIF_SHOWTIP);
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    private Win32.NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _hIcon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = "", szInfoTitle = "",
    };

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            // Version 4: LOWORD(lParam) = event, wParam = anchor x/y in screen coordinates.
            int evt = (int)(lParam.ToInt64() & 0xFFFF);
            var pt = new Win32.POINT((short)(wParam.ToInt64() & 0xFFFF), (short)((wParam.ToInt64() >> 16) & 0xFFFF));
            switch (evt)
            {
                case Win32.NIN_SELECT:
                case Win32.NIN_KEYSELECT:
                    Invoked?.Invoke(pt);
                    return true;
                case Win32.WM_CONTEXTMENU:
                    ContextMenuRequested?.Invoke(pt);
                    return true;
            }
            return false;
        }
        if (msg == _taskbarCreated) // Explorer restarted: our icon is gone
        {
            _added = false;
            if (_hIcon != IntPtr.Zero) Win32.DestroyIcon(_hIcon);
            Show();
            return true;
        }
        return false;
    }

    /// <summary>Creates an HICON of the requested size from .ico bytes (PNG-compressed entries are supported).</summary>
    public static IntPtr CreateIcon(byte[] ico, int size)
    {
        int offset = Win32.LookupIconIdFromDirectoryEx(ico, true, size, size, 0);
        if (offset <= 0) return IntPtr.Zero;
        // The ICONDIRENTRY that owns this image offset tells us its byte length.
        int count = BitConverter.ToUInt16(ico, 4), length = 0;
        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            if (BitConverter.ToInt32(ico, e + 12) == offset) { length = BitConverter.ToInt32(ico, e + 8); break; }
        }
        if (length == 0) return IntPtr.Zero;
        var handle = GCHandle.Alloc(ico, GCHandleType.Pinned);
        try
        {
            return Win32.CreateIconFromResourceEx(handle.AddrOfPinnedObject() + offset, (uint)length, true, 0x00030000, size, size, 0);
        }
        finally { handle.Free(); }
    }

    public void Dispose()
    {
        _window.Message -= OnMessage;
        if (_added)
        {
            var data = Data(0);
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            _added = false;
        }
        if (_hIcon != IntPtr.Zero) { Win32.DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
    }
}
