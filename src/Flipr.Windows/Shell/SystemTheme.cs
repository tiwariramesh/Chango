using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;
using UIColorType = global::Windows.UI.ViewManagement.UIColorType;

namespace Flipr.Windows.Shell;

/// <summary>Reads the Windows app theme and accent colour, and reports changes (WM_SETTINGCHANGE "ImmersiveColorSet").</summary>
public sealed class SystemTheme : IDisposable
{
    private const int WM_SETTINGCHANGE = 0x001A, WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320, WM_SYSCOLORCHANGE = 0x0015;
    private readonly MessageWindow _window;

    public SystemTheme(MessageWindow window)
    {
        _window = window;
        _window.Message += OnMessage;
    }

    /// <summary>Raised on the UI thread when the light/dark preference, accent or contrast settings change.</summary>
    public event Action? Changed;

    public static bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }
    }

    public static Color? AccentColor => Accent(UIColorType.Accent);

    /// <summary>
    /// The accent shades to try, best first, the way Windows itself picks them: on dark surfaces Windows uses lighter
    /// shades of the accent (Light2 for fills), on light surfaces darker ones (Dark1). Empty if unavailable.
    /// </summary>
    public static IReadOnlyList<Color> AccentShades(bool dark)
    {
        var kinds = dark
            ? new[] { UIColorType.AccentLight2, UIColorType.AccentLight1, UIColorType.Accent, UIColorType.AccentLight3 }
            : new[] { UIColorType.AccentDark1, UIColorType.Accent, UIColorType.AccentDark2, UIColorType.AccentDark3 };
        return kinds.Select(Accent).OfType<Color>().ToList();
    }

    private static Color? Accent(UIColorType kind)
    {
        try
        {
            var c = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(kind);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { return null; }
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet"
            || msg == WM_DWMCOLORIZATIONCOLORCHANGED || msg == WM_SYSCOLORCHANGE)
        {
            Changed?.Invoke();
        }
        return false;
    }

    public void Dispose() => _window.Message -= OnMessage;
}
