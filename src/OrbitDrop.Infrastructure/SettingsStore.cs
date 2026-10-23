using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitDrop.Infrastructure;

/// <summary>Loads and saves <see cref="AppSettings"/> as versioned JSON with atomic replacement.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();

    public SettingsStore(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
                Migrate(settings);
                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // Keep the unreadable file for inspection; start from defaults rather than crash.
                string backup = FilePath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
                File.Copy(FilePath, backup, overwrite: true);
                Log.Warn($"Settings file was unreadable ({ex.Message}); saved a copy to {Path.GetFileName(backup)} and reset to defaults.");
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
    }

    private static void Migrate(AppSettings settings)
    {
        // Schema 1 is the first public schema. Future steps go here, e.g.:
        // if (settings.SchemaVersion < 2) { …; settings.SchemaVersion = 2; }
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
    }
}
