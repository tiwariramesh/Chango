namespace Chango.Infrastructure;

/// <summary>Where Chango keeps its own data. Nothing is written anywhere else except user-requested outputs.</summary>
public sealed class AppPaths
{
    public AppPaths(string? roamingRoot = null, string? localRoot = null)
    {
        RoamingDirectory = roamingRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chango");
        LocalDirectory = localRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chango");
    }

    /// <summary>%APPDATA%\Chango: settings (small, follows the user).</summary>
    public string RoamingDirectory { get; }

    /// <summary>%LOCALAPPDATA%\Chango: logs (machine-local).</summary>
    public string LocalDirectory { get; }

    public string SettingsFile => Path.Combine(RoamingDirectory, "settings.json");
    public string LogsDirectory => Path.Combine(LocalDirectory, "logs");
}
