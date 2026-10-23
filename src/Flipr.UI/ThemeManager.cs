using System.Windows;
using System.Windows.Media;
using Flipr.Infrastructure;
using Flipr.Windows.Shell;

namespace Flipr.UI;

/// <summary>
/// Applies Light / Dark / System live, without restarting:
/// <list type="bullet">
/// <item>WPF Fluent <c>ThemeMode</c> for native controls, title bars and Mica (API marked experimental: isolated here);</item>
/// <item>Flipr token dictionaries (Tokens.Light/Dark.xaml), swapped in place, so every DynamicResource updates;</item>
/// <item>optional Windows accent, with a contrast guard that falls back to the Flipr monochrome accent.</item>
/// </list>
/// </summary>
public sealed class ThemeManager
{
    private static readonly Uri LightUri = new("pack://application:,,,/Flipr.UI;component/Themes/Tokens.Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Flipr.UI;component/Themes/Tokens.Dark.xaml");
    private static readonly Uri StylesUri = new("pack://application:,,,/Flipr.UI;component/Themes/Styles.xaml");

    private readonly ResourceDictionary _light = new() { Source = LightUri };
    private readonly ResourceDictionary _dark = new() { Source = DarkUri };
    private readonly ResourceDictionary _accentOverride = new();
    private ResourceDictionary? _activeTokens;

    public static ThemeManager Current { get; } = new();

    public bool IsDark { get; private set; }
    public bool ReduceMotion { get; private set; }
    public bool HighContrast => SystemParameters.HighContrast;

    /// <summary>True when "Use Windows accent" is on but the system accent failed the contrast check.</summary>
    public bool AccentFallbackActive { get; private set; }

    public event Action? Changed;

    /// <summary>Installs the dictionaries into Application resources. Call once at startup.</summary>
    public void Initialize(Application app)
    {
        var merged = app.Resources.MergedDictionaries;
        merged.Add(new ResourceDictionary { Source = StylesUri });
        _activeTokens = _light;
        merged.Add(_activeTokens);
        merged.Add(_accentOverride);
    }

    public void Apply(AppTheme theme, bool useWindowsAccent, bool reduceMotion)
    {
        var app = Application.Current;
        IsDark = theme switch { AppTheme.Dark => true, AppTheme.Light => false, _ => SystemTheme.IsDark };
        ReduceMotion = reduceMotion || !SystemParameters.ClientAreaAnimation;

        app.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;

        var merged = app.Resources.MergedDictionaries;
        var next = IsDark ? _dark : _light;
        if (_activeTokens is not null && !ReferenceEquals(_activeTokens, next))
        {
            int index = merged.IndexOf(_activeTokens);
            if (index >= 0) merged[index] = next; else merged.Add(next);
        }
        _activeTokens = next;

        ApplyAccent(useWindowsAccent);
        Changed?.Invoke();
    }

    private void ApplyAccent(bool useWindowsAccent)
    {
        _accentOverride.Clear();
        AccentFallbackActive = false;
        if (!useWindowsAccent) return;

        // Use the shade of the accent Windows itself uses in this mode (lighter in dark mode, darker in light mode),
        // as long as it stands out against the wheel segments (≥ 3:1); otherwise keep the black and white accent.
        var segment = (Color)_activeTokens![("Orbit.C.WheelSegment")];
        if (PickAccent(SystemTheme.AccentShades(IsDark), segment) is not { } accent) { AccentFallbackActive = true; return; }

        Color onAccent = Luminance(accent) > 0.45 ? Colors.Black : Colors.White;
        Set("Accent", accent);
        Set("AccentHover", Shade(accent, IsDark ? 1.12 : 0.9));
        Set("AccentPressed", Shade(accent, IsDark ? 0.92 : 0.8));
        Set("OnAccent", onAccent);
        Set("AccentText", IsDark ? accent : Shade(accent, 0.85)); // the shade is already the readable one for this mode
    }

    private void Set(string name, Color c)
    {
        _accentOverride[$"Orbit.C.{name}"] = c;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        _accentOverride[$"Orbit.{name}"] = brush;
    }

    /// <summary>The first shade, in Windows' order of preference, that is readable against the wheel segment (≥ 3:1).</summary>
    public static Color? PickAccent(IReadOnlyList<Color> shades, Color segment) =>
        shades.Cast<Color?>().FirstOrDefault(c => Contrast(c!.Value, segment) >= 3.0);

    public static Color Token(string name) => (Color)Application.Current.FindResource($"Orbit.C.{name}");
    public static double Number(string name) => (double)Application.Current.FindResource($"Orbit.{name}");

    public static double Luminance(Color c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static Color Shade(Color c, double f) => Color.FromRgb(
        (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));
}
