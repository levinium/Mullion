using Mullion.App.ViewModels;
using Mullion.Core.Hotkeys;

namespace Mullion.App.Services;

/// <summary>
/// Everything needed to edit the zone layout from a diagram.
/// <para>
/// Its own interface because the editing happens in two places. It began in
/// settings, which meant opening a second window to reshape zones you were
/// already looking at on the main one; the main window now offers the same
/// editor. Splitting it out is what lets one view model serve both rather than
/// the logic being written twice and drifting.
/// </para>
/// </summary>
public interface IZoneEditingHost
{
    /// <summary>
    /// The monitor diagram. Each callback that is supplied turns on one kind of
    /// editing; supply none and the result is the same picture, read-only.
    /// </summary>
    MonitorDiagramViewModel BuildInteractiveDiagram(
        Action<GridPos>? onZoneActivated,
        Action<string, IReadOnlyList<double>>? onSplitChanged,
        Action<string, int>? onZoneCountChanged);

    /// <summary>Set how many zones a display splits into, by hand.</summary>
    void SetDisplayColumns(string slot, int columns);

    /// <summary>Set the relative sizes of a display's zones.</summary>
    void SetDisplayWeights(string slot, IReadOnlyList<double> weights);

    /// <summary>
    /// Open an edit session. Changes apply at once but are not written until it
    /// is committed, so cancelling can put everything back.
    /// </summary>
    void BeginZoneEdit();

    /// <summary>Keep the changes and write them.</summary>
    void CommitZoneEdit();

    /// <summary>Put everything back to where the session started.</summary>
    void CancelZoneEdit();

    /// <summary>Whether anything has been customised, so a reset has a job to do.</summary>
    bool HasCustomZones { get; }

    /// <summary>Discard every custom split and zone count. Keys are left alone.</summary>
    void ResetAllOverrides();

    /// <summary>
    /// Put the keys back where the allocator would have put them, discarding
    /// rebinds. Zone shapes are left alone - the two are separate answers to
    /// "put it back", and someone who has spent time on one should not lose it
    /// undoing the other.
    /// </summary>
    void ResetLayout();

    /// <summary>
    /// Undo and redo for zone shape edits - seam drags, zone counts, and the
    /// resets that clear them. Not key rebinds: those live in the layout rather
    /// than in the overrides, so <see cref="ResetLayout"/> is the way back from
    /// one of those.
    /// </summary>
    bool CanUndoZones { get; }

    bool CanRedoZones { get; }

    void UndoZones();

    void RedoZones();

    /// <summary>
    /// Snap dragged splits to a 5% grid, to simple divisions, and to the
    /// positions where a pane comes out at an exact 16:9 or 3:2 on that display.
    /// Hold Alt while dragging to place a seam freely.
    /// </summary>
    bool SnapSplits { get; }

    void SetSnapSplits(bool value);

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
}
