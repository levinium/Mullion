using Mullion.App.ViewModels;
using Mullion.Core.Abstractions;
using Mullion.Core.Hotkeys;

namespace Mullion.App.Services;

/// <param name="Key">Display label, e.g. "Win+A".</param>
/// <param name="Zone">What that key moves the window to.</param>
/// <param name="Row">Grid position, so a rebind knows what it is moving.</param>
/// <param name="Canceled">
/// The capture was called off rather than failing - Escape, or clicking the
/// same zone again. Distinct from failure because there is nothing to report:
/// leaving "that key is not on the surface" on screen after someone backed
/// out would answer a question they stopped asking.
/// </param>
public sealed record RebindResult(bool Success, string Message, bool Canceled = false);

/// <summary>One display's split, as the settings UI needs to show and change it.</summary>
/// <param name="Slot">Identifies the place on the desk; see DisplaySlot.</param>
/// <param name="Columns">Zones across, or down on a portrait display.</param>
/// <param name="Min">Fewest that stay usable at this size, from the shape analyzer.</param>
/// <param name="Max">Most that stay usable, likewise.</param>
/// <param name="IsCustom">Set by hand rather than derived, so it can be reverted.</param>
public sealed record DisplayCustomization(
    string Slot,
    string Name,
    string Detail,
    int Columns,
    int Min,
    int Max,
    bool IsCustom,
    IReadOnlyList<double> Weights);

public sealed record SettingsSnapshot(
    AutoStartMode AutoStart,
    bool CanConfigureElevatedAutoStart,
    bool IsElevated,
    bool ShowZoneFlash,
    bool AllowSpanningUnions,
    string WinKeySuppression,
    string SurfaceId,
    IReadOnlyList<(string Id, string Name)> AvailableSurfaces,
    string ConfigPath,
    string LogPath,
    bool DragToSnap,
    string DragModifier,
    bool StartInTray,
    string HotkeyModifier,

    /// <summary>The hotkeys that are not zones, as they currently stand.</summary>
    IReadOnlyList<ActionBindingView> Actions,

    /// <summary>Whether the app looks for a newer release once a day.</summary>
    bool CheckForUpdates = true);

/// <param name="Command">Which action, for handing back to the host.</param>
/// <param name="Title">What to call it on screen.</param>
/// <param name="Chord">The chord it answers to, spelled out.</param>
/// <param name="IsCustom">Whether it has been moved off what Mullion ships with.</param>
public sealed record ActionBindingView(string Command, string Title, string Chord, bool IsCustom);

public interface ISettingsHost : IZoneEditingHost
{
    SettingsSnapshot GetSettings();

    /// <summary>Returns an error message when the change could not be applied.</summary>
    string? SetAutoStart(AutoStartMode mode);

    void SetShowZoneFlash(bool value);

    void SetAllowSpanningUnions(bool value);

    /// <summary>Enable drag-to-snap and choose which modifier arms it.</summary>
    void SetDragToSnap(bool enabled, string modifier);

    /// <summary>Open straight to the tray even when launched by hand.</summary>
    void SetStartInTray(bool value);

    /// <summary>Which modifier every zone hotkey is taken with.</summary>
    void SetHotkeyModifier(string value);

    /// <summary>The displays and their splits, and whether each is customized.</summary>
    IReadOnlyList<DisplayCustomization> GetCustomizations();

    /// <summary>The current customizations as a portable document.</summary>
    string ExportLayout(string name);

    /// <summary>Apply a document. Returns an error to show, or null on success.</summary>
    string? ImportLayout(string json);

    void SetWinKeySuppression(string value);

    void SetKeySurface(string surfaceId);

    void RestartElevated();

    void OpenConfigFolder();

    void OpenLogFolder();

    void RerunWizard();

    /// <summary>
    /// Listen for the next chord and give it to an action.
    /// <para>
    /// The same capture the zone diagram uses, and for the same reason it has to
    /// go through the hook: a chord with Win in it never reaches a window, so no
    /// amount of listening in the UI would ever see one.
    /// </para>
    /// </summary>
    void BeginActionRebind(string command, Action<RebindResult> completed);

    /// <summary>Put one action back to the key Mullion ships with.</summary>
    void ResetAction(string command);

    /// <summary>Turn the daily look for a newer release on or off.</summary>
    void SetCheckForUpdates(bool value);

    /// <summary>
    /// Look right now, regardless of when the last look was.
    /// <para>
    /// Deliberately ignores the daily throttle. Someone who presses the button
    /// is asking the question directly, and answering "not yet, try tomorrow"
    /// to a direct question is not a throttle, it is a bug.
    /// </para>
    /// </summary>
    Task<Core.Updates.UpdateVerdict> CheckForUpdatesNow(CancellationToken ct = default);

    /// <summary>Open the page where a new release is downloaded.</summary>
    void OpenUpdatePage(string? url);



}
