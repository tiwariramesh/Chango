using System.Runtime.InteropServices;
using System.Windows.Threading;
using Flipr.Windows.Interop;

namespace Flipr.Windows.DragDrop;

/// <summary>
/// Detects "a shell file drag is in progress" and the trigger-key taps during it.
/// <list type="bullet">
/// <item><b>Primary signal</b>: the shell's <c>SysDragImage</c> window is shown (out-of-context WinEvent; 18/18 in the
/// feasibility run, never on plain clicks). It is <i>latched</i> until the drag button is released, because the shell
/// hides the image while the cursor is over our wheel (feasibility R8).</item>
/// <item><b>OLE drag loop</b>: every OLE drag (<c>DoDragDrop</c>) gives mouse capture to OLE's hidden
/// <c>CLIPBRDWNDCLASS</c> window for as long as the drag lasts. Apps that draw their own drag image (VS Code and other
/// Chromium/Electron apps) never show <c>SysDragImage</c>, but they all go through this loop. Latched the same way.</item>
/// <item><b>Optional</b> "extended detection": a low-level mouse hook heuristic for drag sources that don't use the
/// shell drag image (off by default; it is noisy, feasibility R9).</item>
/// <item>Key and button state is polled at ~60 Hz <b>only while a drag is armed or a wheel is open</b>. There is no
/// keyboard hook, and nothing runs while idle.</item>
/// </list>
/// </summary>
public sealed class DragSessionDetector : IDisposable
{
    private readonly Win32.WinEventProc _winEventProc;
    private readonly Win32.LowLevelProc _mouseProc;
    private readonly DispatcherTimer _poll;
    private IntPtr _showHideHook, _captureHook, _mouseHook;
    private IntPtr _dragImage;
    private bool _imageArmed, _oleArmed, _hookArmed, _hookButtonDown;
    private Win32.POINT _downPoint;
    private bool _prevTrigger, _prevEscape, _prevButton;
    private int _triggerVk = Win32.VK_SHIFT;

    public DragSessionDetector()
    {
        _winEventProc = OnWinEvent;
        _mouseProc = OnMouse;
        _poll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        _poll.Tick += (_, _) => Poll();
    }

    /// <summary>Trigger key went down while a drag was armed (or while a session is open).</summary>
    public event Action? TriggerPressed;
    public event Action? EscapePressed;
    /// <summary>The drag button was released (the drag ended one way or another).</summary>
    public event Action? DragButtonReleased;

    /// <summary>Set by the owner while the wheel is open so polling continues regardless of signals.</summary>
    public bool SessionOpen { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsArmed => Armed && DragButtonDown;

    private bool Armed => _imageArmed || _oleArmed || _hookArmed;

    public static bool DragButtonDown =>
        Win32.IsDown(Win32.GetSystemMetrics(Win32.SM_SWAPBUTTON) != 0 ? Win32.VK_RBUTTON : Win32.VK_LBUTTON);

    public void Start()
    {
        _showHideHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_SHOW, Win32.EVENT_OBJECT_HIDE, IntPtr.Zero, _winEventProc, 0, 0,
            Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        _captureHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_CAPTURESTART, Win32.EVENT_SYSTEM_CAPTURESTART, IntPtr.Zero, _winEventProc, 0, 0,
            Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        Diagnostics.Write(_showHideHook != IntPtr.Zero ? "Drag detection active (shell drag image)" : "Drag detection hook FAILED");
        Diagnostics.Write(_captureHook != IntPtr.Zero ? "Drag detection active (OLE drag loop)" : "OLE drag detection hook FAILED");
    }

    public void SetTriggerKey(bool ctrl) => _triggerVk = ctrl ? Win32.VK_CONTROL : Win32.VK_SHIFT;

    public void SetExtendedDetection(bool enabled)
    {
        if (enabled && _mouseHook == IntPtr.Zero)
        {
            _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, Win32.GetModuleHandle(null), 0);
            Diagnostics.Write(_mouseHook != IntPtr.Zero ? "Extended drag detection on" : $"Extended drag detection FAILED ({Marshal.GetLastWin32Error()})");
        }
        else if (!enabled && _mouseHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            _hookArmed = false;
            Diagnostics.Write("Extended drag detection off");
        }
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == Win32.EVENT_SYSTEM_CAPTURESTART)
        {
            if (!_oleArmed && DragButtonDown && Win32.ClassNameOf(hwnd) == "CLIPBRDWNDCLASS")
            {
                _oleArmed = true;
                Diagnostics.Write("Drag detected (OLE drag loop)");
                StartPolling();
            }
            return;
        }
        if (idObject != Win32.OBJID_WINDOW || idChild != 0) return;
        if (evt == Win32.EVENT_OBJECT_SHOW && Win32.ClassNameOf(hwnd) == "SysDragImage")
        {
            _dragImage = hwnd;
            if (!_imageArmed && DragButtonDown)
            {
                _imageArmed = true;
                StartPolling();
            }
        }
        else if (evt == Win32.EVENT_OBJECT_HIDE && hwnd == _dragImage)
        {
            _dragImage = IntPtr.Zero; // stays latched until the button is released (R8)
        }
    }

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            bool swapped = Win32.GetSystemMetrics(Win32.SM_SWAPBUTTON) != 0;
            int msg = (int)wParam, down = swapped ? Win32.WM_RBUTTONDOWN : Win32.WM_LBUTTONDOWN, up = swapped ? Win32.WM_RBUTTONUP : Win32.WM_LBUTTONUP;
            if (msg == down) { _hookButtonDown = true; _downPoint = info.pt; }
            else if (msg == up) { _hookButtonDown = false; }
            else if (msg == Win32.WM_MOUSEMOVE && _hookButtonDown && !_hookArmed &&
                     (Math.Abs(info.pt.X - _downPoint.X) > Win32.GetSystemMetrics(Win32.SM_CXDRAG) ||
                      Math.Abs(info.pt.Y - _downPoint.Y) > Win32.GetSystemMetrics(Win32.SM_CYDRAG)))
            {
                _hookArmed = true;
                StartPolling();
            }
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void StartPolling()
    {
        if (_poll.IsEnabled) return;
        _prevTrigger = Win32.IsDown(_triggerVk);
        _prevEscape = Win32.IsDown(Win32.VK_ESCAPE);
        _prevButton = DragButtonDown;
        _poll.Start();
    }

    /// <summary>
    /// A drag that starts in one of Flipr's own windows (guide practice photo, Float shelf) has no shell drag image,
    /// so arm the trigger key directly. It disarms when the mouse button is released, like any other drag.
    /// </summary>
    public void ArmInternal()
    {
        _hookArmed = true;
        StartPolling();
    }

    /// <summary>Called by the owner when it opens a wheel without a drag (e.g. preview) so Esc still works.</summary>
    public void EnsurePolling() => StartPolling();

    private void Poll()
    {
        bool button = DragButtonDown, trigger = Win32.IsDown(_triggerVk), escape = Win32.IsDown(Win32.VK_ESCAPE);

        if (Enabled && trigger && !_prevTrigger && (SessionOpen || (Armed && button)))
            TriggerPressed?.Invoke();
        if (escape && !_prevEscape && SessionOpen) EscapePressed?.Invoke();
        if (!button && _prevButton)
        {
            _imageArmed = _oleArmed = _hookArmed = false;
            DragButtonReleased?.Invoke();
        }

        _prevTrigger = trigger; _prevEscape = escape; _prevButton = button;
        if (!button && !SessionOpen && !Armed) _poll.Stop();
    }

    public void Dispose()
    {
        _poll.Stop();
        if (_showHideHook != IntPtr.Zero) Win32.UnhookWinEvent(_showHideHook);
        if (_captureHook != IntPtr.Zero) Win32.UnhookWinEvent(_captureHook);
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _showHideHook = _captureHook = _mouseHook = IntPtr.Zero;
    }
}
