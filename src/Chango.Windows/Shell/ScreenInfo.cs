using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Chango.Windows.Interop;

namespace Chango.Windows.Shell;

public readonly record struct MonitorDetails(Win32.RECT Bounds, Win32.RECT WorkArea, uint Dpi)
{
    public double Scale => Dpi / 96.0;
}

/// <summary>Monitor geometry/DPI (physical pixels, Per-Monitor-V2) and backdrop snapshots for the wheel.</summary>
public static class ScreenInfo
{
    public static Win32.POINT CursorPosition => Win32.GetCursorPos(out var p) ? p : default;

    public static MonitorDetails MonitorAt(Win32.POINT point)
    {
        IntPtr monitor = Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(monitor, ref info);
        if (Win32.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpi, out _) != 0) dpi = 96;
        return new MonitorDetails(info.rcMonitor, info.rcWork, dpi);
    }

    /// <summary>
    /// Copies a screen rectangle with GDI BitBlt <b>without CAPTUREBLT</b>, which leaves out layered windows such as the
    /// drag image. Used once per wheel opening for the frosted backdrop; returns null if the capture is unavailable.
    /// </summary>
    public static BitmapSource? Capture(Win32.RECT rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        IntPtr screen = Win32.GetDC(IntPtr.Zero), mem = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            mem = Win32.CreateCompatibleDC(screen);
            bitmap = Win32.CreateCompatibleBitmap(screen, rect.Width, rect.Height);
            old = Win32.SelectObject(mem, bitmap);
            if (!Win32.BitBlt(mem, 0, 0, rect.Width, rect.Height, screen, rect.Left, rect.Top, Win32.SRCCOPY)) return null;
            Win32.SelectObject(mem, old);
            old = IntPtr.Zero;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"Backdrop capture failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (old != IntPtr.Zero) Win32.SelectObject(mem, old);
            if (bitmap != IntPtr.Zero) Win32.DeleteObject(bitmap);
            if (mem != IntPtr.Zero) Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
