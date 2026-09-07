using Mullion.App.ViewModels;
using Mullion.Core.Abstractions;
using Mullion.Core.Hotkeys;

namespace Mullion.App.Services;

/// <param name="Key">Display label, e.g. "Win+A".</param>
/// <param name="Zone">What that key moves the window to.</param>
/// <param name="Row">Grid position, so a rebind knows what it is moving.</param>
public sealed record BindingEntry(string Key, string Zone, int Row, int Col);

public sealed record RebindResult(bool Success, string Message);

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
    IReadOnlyList<BindingEntry> Bindings,
    string ConfigPath,
    string LogPath,
    bool DragToSnap,
    string DragModifier,
    bool StartInTray,
    string HotkeyModifier);

public interface ISettingsHost
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

    /// <summary>The displays and their splits, for the customisation UI.</summary>
    IReadOnlyList<DisplayCustomization> GetCustomizations();

    /// <summary>Set how many zones a display splits into, by hand.</summary>
    void SetDisplayColumns(string slot, int columns);

    /// <summary>Set the relative sizes of a display's zones.</summary>
    void SetDisplayWeights(string slot, IReadOnlyList<double> weights);

    /// <summary>Forget one display's customisation, or all of them.</summary>
    void ResetDisplayOverride(string slot);

    void ResetAllOverrides();

    /// <summary>The current customisations as a portable document.</summary>
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
    /// Listen for the next chord and move the zone at <paramref name="row"/>,
    /// <paramref name="col"/> onto that key.
    /// <para>
    /// Capture runs through the keyboard hook because the UI framework never
    /// sees Win-modified keys, so this cannot be a plain KeyDown handler.
    /// </para>
    /// </summary>
    void BeginRebind(int row, int col, Action<RebindResult> completed);

    void CancelRebind();

    /// <summary>
    /// Undo and redo for zone shape edits - seam drags, zone counts, and the
    /// resets that clear them. Not key rebinds: those live in the layout rather
    /// than in the overrides, so "Reset keys to default" is the way back.
    /// </summary>
    /// <summary>
    /// Snap dragged splits to a 5% grid, to simple divisions, and to the
    /// positions where a pane comes out at an exact 16:9 or 3:2 on that
    /// display. Hold Alt while dragging to place a seam freely.
    /// </summary>
    bool SnapSplits { get; }

    void SetSnapSplits(bool value);

    bool CanUndoZones { get; }

    bool CanRedoZones { get; }

    void UndoZones();

    void RedoZones();

    /// <summary>
    /// Put the keys back where the allocator would have put them, discarding
    /// rebinds. Zone shapes are left alone - the two are separate answers to
    /// "put it back", and a user who has spent time on one should not lose it
    /// undoing the other.
    /// </summary>
    void ResetLayout();

    /// <summary>
    /// The monitor diagram, wired so clicking a zone starts a rebind and
    /// dragging a seam reshapes the split. Built by the host because only it
    /// holds the current displays and layout.
    /// </summary>
    MonitorDiagramViewModel BuildInteractiveDiagram(
        Action<GridPos> onZoneActivated,
        Action<string, IReadOnlyList<double>> onSplitChanged,
        Action<string, int> onZoneCountChanged);
}
