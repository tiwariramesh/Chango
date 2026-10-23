using Microsoft.Win32;

namespace Chango.Windows.Shell;

/// <summary>Per-user "launch at sign-in" via HKCU\…\Run. No admin rights; the installer removes it on uninstall.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Chango";

    public static bool IsEnabled
    {
        get
        {
            if (AppPackage.IsPackaged) return AppPackage.StartupEnabled; // Store version: the package's startup task
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path");

    public static void Set(bool enabled)
    {
        if (AppPackage.IsPackaged) { AppPackage.SetStartup(enabled); return; }
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{ExecutablePath}\" --background");
        else if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName);
    }
}
