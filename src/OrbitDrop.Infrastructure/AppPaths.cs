namespace OrbitDrop.Infrastructure;

/// <summary>Where OrbitDrop keeps its own data. Nothing is written anywhere else except user-requested outputs.</summary>
public sealed class AppPaths
{
    public AppPaths(string? roamingRoot = null, string? localRoot = null)
    {
        RoamingDirectory = roamingRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrbitDrop");
        LocalDirectory = localRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitDrop");
    }

    /// <summary>%APPDATA%\OrbitDrop: settings (small, follows the user).</summary>
    public string RoamingDirectory { get; }

    /// <summary>%LOCALAPPDATA%\OrbitDrop: logs (machine-local).</summary>
    public string LocalDirectory { get; }

    public string SettingsFile => Path.Combine(RoamingDirectory, "settings.json");
    public string LogsDirectory => Path.Combine(LocalDirectory, "logs");
}
