using System.Runtime.Versioning;
using System.Text.Json;
using Mullion.Core.Hotkeys;

namespace Mullion.Platform.Windows.Hotkeys;

public enum ConflictSeverity
{
    Info,
    Warning,
    Blocking,
}

public sealed record HookConflict(
    ConflictSeverity Severity,
    string Source,
    string Summary,
    string Advice);

/// <summary>
/// Detects other software competing for the same keys.
/// <para>
/// This exists because of a failure mode that is invisible until it bites:
/// low-level keyboard hooks are called in install order, most recent first.
/// Mullion wins against a tool that started at boot only because Mullion
/// started later. If that tool's engine restarts - an update, or the user
/// toggling the module - it becomes the newer hook and silently claims the key
/// first. Mullion would then appear to work reliably until it abruptly did not,
/// for reasons that look random from the outside.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class HookConflictDetector
{
    private static readonly string[] KnownHookApps =
    [
        "PowerToys.KeyboardManagerEngine",
        "AutoHotkey",
        "AutoHotkey64",
        "AutoHotkeyU64",
        "SharpKeys",
    ];

    public static IReadOnlyList<HookConflict> Detect(IEnumerable<ushort> boundScanCodes, ChordModifiers modifiers)
    {
        var conflicts = new List<HookConflict>();
        var scans = boundScanCodes.ToHashSet();

        conflicts.AddRange(DetectPowerToysRemaps(scans, modifiers));
        conflicts.AddRange(DetectOtherHookApps());

        return conflicts;
    }

    /// <summary>
    /// Read PowerToys Keyboard Manager's own configuration rather than guessing.
    /// A remap of a key Mullion also binds is a direct collision.
    /// </summary>
    private static IEnumerable<HookConflict> DetectPowerToysRemaps(
        HashSet<ushort> boundScans, ChordModifiers modifiers)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "PowerToys", "Keyboard Manager", "default.json");

        if (!File.Exists(path)) yield break;

        var running = System.Diagnostics.Process
            .GetProcessesByName("PowerToys.KeyboardManagerEngine").Length > 0;

        List<string> collisions;

        try
        {
            collisions = ReadCollidingRemaps(path, boundScans, modifiers);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            yield break;
        }

        if (collisions.Count == 0) yield break;

        yield return new HookConflict(
            running ? ConflictSeverity.Warning : ConflictSeverity.Info,
            "PowerToys Keyboard Manager",
            $"Also remaps {string.Join(", ", collisions)}.",
            running
                ? "Mullion currently intercepts these first because it started more recently, but that " +
                  "is not stable: if PowerToys restarts or Keyboard Manager is toggled, it will claim " +
                  "these keys instead and Mullion will stop responding to them. Remove the remaps in " +
                  "PowerToys, since Mullion replaces the workaround they were added for."
                : "These remaps are configured but Keyboard Manager is not running. If it starts later " +
                  "it will claim these keys ahead of Mullion.");
    }

    private static List<string> ReadCollidingRemaps(
        string path, HashSet<ushort> boundScans, ChordModifiers modifiers)
    {
        var result = new List<string>();

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("remapShortcuts", out var shortcuts)) return result;
        if (!shortcuts.TryGetProperty("global", out var global)) return result;

        foreach (var entry in global.EnumerateArray())
        {
            if (!entry.TryGetProperty("originalKeys", out var keysElement)) continue;

            var parts = (keysElement.GetString() ?? string.Empty).Split(';');
            if (parts.Length < 2) continue;

            var mods = ChordModifiers.None;
            var terminal = 0;

            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var vk)) continue;
                switch (vk)
                {
                    case 0x5B or 0x5C: mods |= ChordModifiers.Win; break;
                    case 0x10 or 0xA0 or 0xA1: mods |= ChordModifiers.Shift; break;
                    case 0x11 or 0xA2 or 0xA3: mods |= ChordModifiers.Control; break;
                    case 0x12 or 0xA4 or 0xA5: mods |= ChordModifiers.Alt; break;
                    default: terminal = vk; break;
                }
            }

            if (mods != modifiers || terminal == 0) continue;

            var scan = ScanCodeForVirtualKey((ushort)terminal);
            if (scan != 0 && boundScans.Contains(scan))
                result.Add($"{DescribeModifiers(mods)}+{(char)terminal}");
        }

        return result;
    }

    /// <summary>
    /// Other processes known to install low-level keyboard hooks. Their presence
    /// is not itself a problem, but it explains erratic behavior when it happens.
    /// </summary>
    private static IEnumerable<HookConflict> DetectOtherHookApps()
    {
        foreach (var name in KnownHookApps)
        {
            if (name.StartsWith("PowerToys", StringComparison.Ordinal)) continue;
            if (System.Diagnostics.Process.GetProcessesByName(name).Length == 0) continue;

            yield return new HookConflict(
                ConflictSeverity.Info,
                name,
                "Also installs a low-level keyboard hook.",
                "If hotkeys behave unpredictably, check whether this tool binds the same keys. " +
                "Whichever hook was installed most recently sees each key first.");
        }
    }

    /// <summary>
    /// Letter and digit rows only, which covers every key surface Mullion ships.
    /// MapVirtualKey would be more general but pulls in layout-dependent
    /// behavior that is the very thing scan codes exist to avoid.
    /// </summary>
    private static ushort ScanCodeForVirtualKey(ushort vk) => vk switch
    {
        0x41 => 0x1E, 0x42 => 0x30, 0x43 => 0x2E, 0x44 => 0x20, 0x45 => 0x12,
        0x46 => 0x21, 0x47 => 0x22, 0x48 => 0x23, 0x49 => 0x17, 0x4A => 0x24,
        0x4B => 0x25, 0x4C => 0x26, 0x4D => 0x32, 0x4E => 0x31, 0x4F => 0x18,
        0x50 => 0x19, 0x51 => 0x10, 0x52 => 0x13, 0x53 => 0x1F, 0x54 => 0x14,
        0x55 => 0x16, 0x56 => 0x2F, 0x57 => 0x11, 0x58 => 0x2D, 0x59 => 0x15,
        0x5A => 0x2C,
        _ => 0,
    };

    private static string DescribeModifiers(ChordModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ChordModifiers.Win)) parts.Add("Win");
        if (mods.HasFlag(ChordModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ChordModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ChordModifiers.Shift)) parts.Add("Shift");
        return parts.Count == 0 ? "(none)" : string.Join("+", parts);
    }
}
