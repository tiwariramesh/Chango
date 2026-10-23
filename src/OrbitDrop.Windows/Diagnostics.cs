namespace OrbitDrop.Windows;

/// <summary>Logging hook for the Windows layer (wired to the app log by OrbitDrop.App).</summary>
public static class Diagnostics
{
    public static Action<string>? Sink { get; set; }

    public static void Write(string message) => Sink?.Invoke(message);
}
