using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mullion.Core.Model;

namespace Mullion.Platform.Windows.Windows;

/// <summary>What PowerToys FancyZones is currently doing, as far as we can tell.</summary>
/// <param name="Running">Its process is up.</param>
/// <param name="ShiftDrag">It is armed by holding Shift, rather than always on.</param>
/// <param name="Collides">It reacts to the same gesture Mullion is configured for.</param>
/// <param name="DisabledInSettings">
/// Switched off in PowerToys' settings but still running. PowerToys reads the
/// enabled flags at startup and does not watch them, so the change is real but
/// takes effect only when PowerToys next restarts.
/// </param>
public sealed record FancyZonesState(
    bool Running, bool ShiftDrag, bool Collides, bool DisabledInSettings);

/// <summary>
/// Detects PowerToys FancyZones, which is the one program guaranteed to fight
/// drag-to-snap: it does the same job on the same gesture.
/// <para>
/// Two zone managers reacting to one drag do not merely duplicate each other -
/// both move the window, and whichever writes last wins, so the result changes
/// from drag to drag for no visible reason. It looks like Mullion being
/// unreliable rather than like a conflict, which is exactly why it is worth
/// naming rather than leaving the user to work out.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class FancyZones
{
    private const string ProcessName = "PowerToys.FancyZones";
    private const string ModuleKey = "FancyZones";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "PowerToys", "settings.json");

    private static string ModuleSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "PowerToys", "FancyZones", "settings.json");

    public static FancyZonesState Detect(DragModifier mullionModifier)
    {
        var running = false;

        try { running = Process.GetProcessesByName(ProcessName).Length > 0; }
        catch { /* enumeration can fail under lockdown policies; assume not running */ }

        if (!running) return new FancyZonesState(false, false, false, false);

        var shiftDrag = ReadShiftDrag();

        // Shift-drag collides only when Mullion is on Shift too. With FancyZones
        // always-on, every drag is its drag, so any Mullion modifier collides.
        var collides = shiftDrag
            ? mullionModifier == DragModifier.Shift
            : true;

        return new FancyZonesState(true, shiftDrag, collides, IsDisabledInSettings());
    }

    /// <summary>
    /// Open PowerToys so the user can switch the module off there.
    /// </summary>
    /// <remarks>
    /// Mullion used to write enabled.FancyZones straight into PowerToys'
    /// settings.json. It does not work: PowerToys owns that file and writes its
    /// own in-memory state back over it when it restarts, so the edit survives
    /// only until the next restart - which is exactly when it was supposed to
    /// take effect. Worse, the module keeps running meanwhile, so the write can
    /// look successful while changing nothing.
    ///
    /// Asking PowerToys to show its settings leaves the change to the program
    /// that owns it, and leaves Mullion only having to notice the result.
    /// </remarks>
    public static bool OpenPowerToysSettings(out string? error)
    {
        error = null;

        // The running process cannot be asked for its path: PowerToys usually
        // runs elevated, and a medium-integrity process cannot read that.
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerToys", "PowerToys.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "PowerToys", "PowerToys.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerToys", "PowerToys.exe"),
        ];

        var exe = candidates.FirstOrDefault(File.Exists);
        if (exe is null)
        {
            error = "Could not find PowerToys.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
    /// <summary>Whether the module is already switched off in PowerToys' settings.</summary>
    private static bool IsDisabledInSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));

            return doc.RootElement.TryGetProperty("enabled", out var enabled)
                   && enabled.TryGetProperty(ModuleKey, out var value)
                   && value.ValueKind == JsonValueKind.False;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether FancyZones is armed by Shift rather than being always on.</summary>
    private static bool ReadShiftDrag()
    {
        try
        {
            var path = ModuleSettingsPath;
            if (!File.Exists(path)) return true;   // the shipped default

            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            return !doc.RootElement.TryGetProperty("properties", out var properties)
                   || !properties.TryGetProperty("fancyzones_shiftDrag", out var shiftDrag)
                   || !shiftDrag.TryGetProperty("value", out var value)
                   || value.ValueKind != JsonValueKind.False;
        }
        catch
        {
            return true;
        }
    }
}
