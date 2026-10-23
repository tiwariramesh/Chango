using System.Windows.Interop;

namespace Flipr.Windows.Shell;

/// <summary>
/// Hidden top-level window that receives tray callbacks and broadcast messages
/// (TaskbarCreated, WM_SETTINGCHANGE). A message-only window would miss broadcasts.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("Flipr.MessageWindow") { Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
    }

    private const int WM_COPYDATA = 0x004A;

    public IntPtr Handle => _source.Handle;

    /// <summary>(msg, wParam, lParam) → return true if handled.</summary>
    public event Func<int, IntPtr, IntPtr, bool>? Message;

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (Message is { } handlers)
            foreach (Func<int, IntPtr, IntPtr, bool> h in handlers.GetInvocationList())
                if (h(msg, wParam, lParam)) { handled = true; return msg == WM_COPYDATA ? 1 : IntPtr.Zero; } // WM_COPYDATA: TRUE = received
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
