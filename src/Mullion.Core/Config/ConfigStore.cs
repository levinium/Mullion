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

    /// <summary>
    /// Where a simulated desk keeps its settings: beside the real config, never
    /// in it. Kept in their own folder so the real one is the only file at the
    /// top level and nobody has to work out which of a dozen is theirs.
    /// </summary>
    public static string ForSimulation(string topologyId) => Path.Combine(
        Path.GetDirectoryName(DefaultPath)!,
        "simulations",
        $"{topologyId}.json");

    public string Path_ => _path;

    /// <summary>Diagnostics from the most recent load, e.g. a fallback to backup.</summary>
    public IReadOnlyList<string> LoadNotes { get; private set; } = [];

    /// <summary>
    /// True when the last load upgraded an older schema, so the caller can write
    /// it back once instead of migrating on every launch.
    /// </summary>
    public bool MigratedOnLoad { get; private set; }

    public AppConfig Load()
    {
        var notes = new List<string>();
        MigratedOnLoad = false;

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

                var migrated = Migrate(config, notes);
                MigratedOnLoad = migrated.SchemaVersion != config.SchemaVersion;
                LoadNotes = notes;
                return migrated;
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

        var from = config.SchemaVersion;

        // v1 -> v2: zone names are persisted, so changing how they are generated
        // does not reach a profile that is already saved. Rewriting them here is
        // what makes the spelling change actually visible to existing users
        // rather than only to new ones.
        if (config.SchemaVersion < 2)
        {
            config = config with
            {
                Profiles = [.. config.Profiles.Select(p => p with
                {
                    Zones = [.. p.Zones.Select(z => z with { Name = Americanise(z.Name) })],
                })],
            };
        }

        // v2 -> v3: zone names dropped the display-name prefix, since the
        // diagram labels each display directly. Stored names still carry it.
        if (config.SchemaVersion < 3)
        {
            config = config with
            {
                Profiles = [.. config.Profiles.Select(p => p with
                {
                    Zones = [.. p.Zones.Select(z => z with { Name = StripDisplayPrefix(z.Name, p) })],
                })],
            };
        }

        notes.Add($"Migrated config from schema v{from} to v{AppConfig.CurrentSchemaVersion}.");
        return config with { SchemaVersion = AppConfig.CurrentSchemaVersion };
    }

    /// <summary>
    /// Remove a leading display name from a zone name, capitalising what is
    /// left. A zone whose name IS the display name is untouched: that is the
    /// whole-display case, where the display name is the only sensible label.
    /// </summary>
    private static string StripDisplayPrefix(string name, ProfileRecord profile)
    {
        foreach (var display in profile.Displays)
        {
            var prefix = display.FriendlyName + " ";
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var rest = name[prefix.Length..].Trim();
            if (rest.Length == 0) return name;

            return char.ToUpperInvariant(rest[0]) + rest[1..];
        }

        return name;
    }

    private static string Americanise(string name) => name
        .Replace("centre", "center", StringComparison.Ordinal)
        .Replace("Centre", "Center", StringComparison.Ordinal);
}
