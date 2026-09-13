using System.Runtime.Versioning;
using System.Text.Json;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Which PowerToys modules are switched on, read from PowerToys' own settings.
/// <para>
/// Presence is not the same as participation. PowerToys ships as one install
/// with two dozen modules, most of them off, and looking only at whether its
/// files exist warns people about a feature they have deliberately turned off -
/// which teaches them to ignore the warning, including the time it is right.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class PowerToysModules
{
    /// <summary>The name PowerToys stores for Keyboard Manager, spaces and all.</summary>
    public const string KeyboardManager = "Keyboard Manager";

    public const string FancyZones = "FancyZones";

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "PowerToys", "settings.json");

    /// <summary>
    /// Whether a module is enabled, or null when PowerToys cannot be asked.
    /// <para>
    /// Three answers, not two. A missing or unreadable settings file means "no
    /// idea", which is different from "off" - and the caller should go on
    /// warning in that case rather than fall silent because a file could not be
    /// parsed.
    /// </para>
    /// </summary>
    public static bool? IsEnabled(string module)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;

            return Read(File.ReadAllText(SettingsPath), module);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The same question asked of settings already in hand. Separated so the
    /// parsing can be tested against real PowerToys files without one being
    /// installed - and because the file belongs to another program, which may
    /// reshape it whenever it likes.
    /// </summary>
    public static bool? Read(string? settingsJson, string module)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("enabled", out var enabled)) return null;
            if (enabled.ValueKind != JsonValueKind.Object) return null;
            if (!enabled.TryGetProperty(module, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a module is definitely off. The question a warning should ask:
    /// stay quiet only when PowerToys says so outright, never on a guess.
    /// </summary>
    public static bool IsOff(string module) => IsEnabled(module) == false;
}
