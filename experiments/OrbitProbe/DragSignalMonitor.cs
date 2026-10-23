using System.Runtime.InteropServices;
using OrbitProbe.Native;

namespace OrbitProbe;

public enum DragSignal
{
    /// <summary>H-3a: EVENT_SYSTEM_DRAGDROPSTART / END WinEvents.</summary>
    OleDragDropEvent,
    /// <summary>H-3b: the shell's "SysDragImage" window being shown / hidden.</summary>
    ShellDragImage,
    /// <summary>H-3c: low-level mouse hook heuristic (left button held + moved past SM_CXDRAG).</summary>
    MouseHookHeuristic,
}

/// <summary>
/// Observes candidate "a drag has started" signals without injecting into any process.
/// All callbacks arrive on the UI thread's message loop (hooks are installed from it).
/// </summary>
public sealed class DragSignalMonitor : IDisposable
{
    private readonly Win32.WinEventProc _winEventProc;   // keep delegates alive: native code holds them
    private readonly Win32.LowLevelProc _mouseProc;
    private IntPtr _dragDropHook, _showHideHook, _mouseHook;
    private IntPtr _dragImageHwnd;
    private Win32.POINT _downPt;
    private bool _buttonDown;
    private readonly Dictionary<DragSignal, bool> _active = new()
    {
        [DragSignal.OleDragDropEvent] = false,
        [DragSignal.ShellDragImage] = false,
        [DragSignal.MouseHookHeuristic] = false,
    };

    /// <summary>Raised when a signal flips. Args: signal, active, detail.</summary>
    public event Action<DragSignal, bool, string>? SignalChanged;

    /// <summary>Raised for each left-button press/release seen by the LL hook (includes LLMHF_INJECTED flag).</summary>
    public event Action<bool, bool>? LeftButton; // (down, injected)

    public bool LastMouseInputInjected { get; private set; }

    public DragSignalMonitor()
    {
        _winEventProc = OnWinEvent;
        _mouseProc = OnMouseLowLevel;
    }

    public bool IsActive(DragSignal s) => _active[s];

    public void Start(bool useMouseHook)
    {
        const uint flags = Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS;
        _dragDropHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_DRAGDROPSTART, Win32.EVENT_SYSTEM_DRAGDROPEND,
            IntPtr.Zero, _winEventProc, 0, 0, flags);
        _showHideHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_SHOW, Win32.EVENT_OBJECT_HIDE,
            IntPtr.Zero, _winEventProc, 0, 0, flags);

        ProbeLog.Info($"WinEvent hooks: dragdrop={(_dragDropHook != IntPtr.Zero ? "ok" : "FAILED")}, " +
                      $"show/hide={(_showHideHook != IntPtr.Zero ? "ok" : "FAILED")}");
        SetMouseHook(useMouseHook);
    }

    public void SetMouseHook(bool enabled)
    {
        if (enabled && _mouseHook == IntPtr.Zero)
        {
            _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, Win32.GetModuleHandle(null), 0);
            ProbeLog.Info(_mouseHook != IntPtr.Zero ? "Low-level mouse hook installed"
                                                     : $"Low-level mouse hook FAILED ({Marshal.GetLastWin32Error()})");
        }
        else if (!enabled && _mouseHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            Set(DragSignal.MouseHookHeuristic, false, "hook removed");
            ProbeLog.Info("Low-level mouse hook removed");
        }
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        switch (evt)
        {
            case Win32.EVENT_SYSTEM_DRAGDROPSTART:
                Set(DragSignal.OleDragDropEvent, true, $"from {Describe(hwnd)}");
                break;
            case Win32.EVENT_SYSTEM_DRAGDROPEND:
                Set(DragSignal.OleDragDropEvent, false, $"from {Describe(hwnd)}");
                break;
            case Win32.EVENT_OBJECT_SHOW when idObject == Win32.OBJID_WINDOW && idChild == 0:
                if (Win32.ClassNameOf(hwnd) == "SysDragImage")
                {
                    _dragImageHwnd = hwnd;
                    Set(DragSignal.ShellDragImage, true, $"hwnd 0x{hwnd:X}");
                }
                break;
            case Win32.EVENT_OBJECT_HIDE when idObject == Win32.OBJID_WINDOW && idChild == 0:
                if (hwnd == _dragImageHwnd && _dragImageHwnd != IntPtr.Zero)
                {
                    _dragImageHwnd = IntPtr.Zero;
                    Set(DragSignal.ShellDragImage, false, $"hwnd 0x{hwnd:X}");
                }
                break;
        }
    }

    private IntPtr OnMouseLowLevel(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            bool injected = (info.flags & 0x1) != 0; // LLMHF_INJECTED
            int msg = (int)wParam;
            if (msg == Win32.WM_LBUTTONDOWN)
            {
                _buttonDown = true;
                _downPt = info.pt;
                LastMouseInputInjected = injected;
                LeftButton?.Invoke(true, injected);
            }
            else if (msg == Win32.WM_LBUTTONUP)
            {
                _buttonDown = false;
                LeftButton?.Invoke(false, injected);
                Set(DragSignal.MouseHookHeuristic, false, "button up");
            }
            else if (msg == Win32.WM_MOUSEMOVE && _buttonDown && !_active[DragSignal.MouseHookHeuristic])
            {
                int dx = Math.Abs(info.pt.X - _downPt.X), dy = Math.Abs(info.pt.Y - _downPt.Y);
                if (dx > Win32.GetSystemMetrics(Win32.SM_CXDRAG) || dy > Win32.GetSystemMetrics(Win32.SM_CYDRAG))
                    Set(DragSignal.MouseHookHeuristic, true, $"moved {dx},{dy}px{(injected ? " (INJECTED input)" : "")}");
            }
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void Set(DragSignal s, bool active, string detail)
    {
        if (_active[s] == active) return;
        _active[s] = active;
        SignalChanged?.Invoke(s, active, detail);
    }

    private static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(null hwnd)";
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        string proc;
        try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { proc = $"pid {pid}"; }
        return $"{Win32.ClassNameOf(hwnd)} in {proc}";
    }

    public void Dispose()
    {
        if (_dragDropHook != IntPtr.Zero) Win32.UnhookWinEvent(_dragDropHook);
        if (_showHideHook != IntPtr.Zero) Win32.UnhookWinEvent(_showHideHook);
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _dragDropHook = _showHideHook = _mouseHook = IntPtr.Zero;
    }
}
