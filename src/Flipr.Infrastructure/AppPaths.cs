namespace Flipr.Infrastructure;

/// <summary>Where Flipr keeps its own data. Nothing is written anywhere else except user-requested outputs.</summary>
public sealed class AppPaths
{
    public AppPaths(string? roamingRoot = null, string? localRoot = null)
    {
        RoamingDirectory = roamingRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flipr");
        LocalDirectory = localRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flipr");
    }

    /// <summary>%APPDATA%\Flipr: settings (small, follows the user).</summary>
    public string RoamingDirectory { get; }

    /// <summary>%LOCALAPPDATA%\Flipr: logs (machine-local).</summary>
    public string LocalDirectory { get; }

    public string SettingsFile => Path.Combine(RoamingDirectory, "settings.json");
    public string LogsDirectory => Path.Combine(LocalDirectory, "logs");
}
