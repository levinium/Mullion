using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mullion.Core.Config;

/// <summary>
/// Loads and saves config.json.
/// <para>
/// Saves are atomic (temp file, then File.Replace with a backup) because a
/// crash or power loss part-way through a write would otherwise leave a
/// truncated file, and the app would come back with every profile gone.
/// </para>
/// </summary>
public sealed class ConfigStore
{
    private readonly string _path;
    private readonly string _backupPath;
    private readonly JsonSerializerOptions _options;

    public ConfigStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _backupPath = _path + ".bak";

        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new PxRectJsonConverter(), new NormRectJsonConverter() },
        };
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Mullion",
        "config.json");

    public string Path_ => _path;

    /// <summary>Diagnostics from the most recent load, e.g. a fallback to backup.</summary>
    public IReadOnlyList<string> LoadNotes { get; private set; } = [];

    public AppConfig Load()
    {
        var notes = new List<string>();

        foreach (var candidate in new[] { _path, _backupPath })
        {
            if (!File.Exists(candidate)) continue;

            try
            {
                var json = File.ReadAllText(candidate);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _options);

                if (config is null)
                {
                    notes.Add($"{candidate} deserialized to null; ignoring.");
                    continue;
                }

                if (candidate == _backupPath)
                    notes.Add("Primary config was unreadable; recovered from backup.");

                LoadNotes = notes;
                return Migrate(config, notes);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                notes.Add($"Could not read {candidate}: {e.Message}");
            }
        }

        LoadNotes = notes;
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, _options));

        if (File.Exists(_path))
        {
            File.Replace(temp, _path, _backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, _path, overwrite: true);
        }
    }

    /// <summary>
    /// Upgrade older schemas in place. Called on every load so a config written
    /// by a newer build is still usable after a downgrade, rather than discarded.
    /// </summary>
    private static AppConfig Migrate(AppConfig config, List<string> notes)
    {
        if (config.SchemaVersion == AppConfig.CurrentSchemaVersion) return config;

        if (config.SchemaVersion > AppConfig.CurrentSchemaVersion)
        {
            notes.Add(
                $"Config schema v{config.SchemaVersion} is newer than this build understands " +
                $"(v{AppConfig.CurrentSchemaVersion}); unknown settings will be preserved but ignored.");
            return config;
        }

        notes.Add($"Migrated config from schema v{config.SchemaVersion} to v{AppConfig.CurrentSchemaVersion}.");
        return config with { SchemaVersion = AppConfig.CurrentSchemaVersion };
    }
}
