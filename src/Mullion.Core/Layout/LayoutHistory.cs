namespace Mullion.Core.Layout;

/// <summary>
/// Everything an undo has to be able to put back.
/// <para>
/// Two things, because a zone has two kinds of change: its shape, which lives
/// in the override set, and the key it answers to, which lives on the layout.
/// The history knew only about the first, so rebinding a key left Undo greyed
/// out and there was no way back from it - the one edit in the editor that
/// could not be undone.
/// </para>
/// </summary>
/// <param name="Overrides">Zone shapes and counts.</param>
/// <param name="Layout">
/// The layout as it stood, for the keys on it. Null before one has been built.
/// </param>
public sealed record LayoutSnapshot(
    IReadOnlyList<DisplayOverride> Overrides,
    LayoutResult? Layout);

/// <summary>
/// Undo and redo over a display's zone customizations.
/// <para>
/// Snapshots of the whole override set rather than a log of individual edits.
/// An edit here is not independent of its neighbors - dragging a seam changes
/// two zones at once, and changing a zone count throws away weights meant for
/// the old one - so replaying an inverse operation would have to reconstruct
/// state the operation itself destroyed. The override set is small enough that
/// keeping copies of it costs nothing worth counting.
/// </para>
/// </summary>
public sealed class LayoutHistory
{
    /// <summary>
    /// How far back it is worth being able to go. Deep enough that a session of
    /// nudging seams stays undoable, shallow enough to bound the memory.
    /// </summary>
    private const int Depth = 64;

    private readonly List<LayoutSnapshot> _past = [];
    private readonly List<LayoutSnapshot> _future = [];

    private LayoutSnapshot _present;

    public LayoutHistory(LayoutSnapshot? initial = null) =>
        _present = initial is null ? new LayoutSnapshot([], null) : Copy(initial);

    /// <summary>
    /// Taken by value, not held by reference. A caller that went on mutating
    /// the list it handed over would silently rewrite history behind it.
    /// <para>
    /// Only the override set needs copying: a layout is replaced rather than
    /// edited in place everywhere it is produced.
    /// </para>
    /// </summary>
    private static LayoutSnapshot Copy(LayoutSnapshot state) =>
        state with { Overrides = [.. state.Overrides] };

    public bool CanUndo => _past.Count > 0;

    public bool CanRedo => _future.Count > 0;

    /// <summary>The state as it now stands.</summary>
    public LayoutSnapshot Present => _present;

    /// <summary>
    /// Take note of an edit that has just happened.
    /// <para>
    /// An edit that changes nothing is not recorded. Dragging a seam reports on
    /// release even when the pointer never moved, and a click that did nothing
    /// should not cost an undo press to get past.
    /// </para>
    /// </summary>
    public void Record(LayoutSnapshot state)
    {
        if (Same(state, _present)) return;

        _past.Add(_present);
        if (_past.Count > Depth) _past.RemoveAt(0);

        // A new edit abandons the branch that was undone away from. Keeping it
        // would let redo jump to a state that no longer follows from this one.
        _future.Clear();

        _present = Copy(state);
    }

    public LayoutSnapshot? Undo()
    {
        if (_past.Count == 0) return null;

        _future.Add(_present);
        _present = _past[^1];
        _past.RemoveAt(_past.Count - 1);

        return _present;
    }

    public LayoutSnapshot? Redo()
    {
        if (_future.Count == 0) return null;

        _past.Add(_present);
        _present = _future[^1];
        _future.RemoveAt(_future.Count - 1);

        return _present;
    }

    /// <summary>
    /// Start again from a given state, forgetting everything.
    /// <para>
    /// For a display change: the overrides that were undoable described monitors
    /// that may no longer be attached, and undoing onto them would apply a
    /// layout for a desk that is not there.
    /// </para>
    /// </summary>
    public void Reset(LayoutSnapshot state)
    {
        _past.Clear();
        _future.Clear();
        _present = Copy(state);
    }

    /// <summary>
    /// Two snapshots are the same when neither the shapes nor the keys differ.
    /// A rebind changes only the second, which is why comparing override sets
    /// alone read every rebind as "nothing happened".
    /// </summary>
    private static bool Same(LayoutSnapshot a, LayoutSnapshot b) =>
        Same(a.Overrides, b.Overrides) && SameKeys(a.Layout, b.Layout);

    /// <summary>
    /// Null means "no layout was recorded", which is not the same as "the same
    /// keys". Written the other way round it read every comparison against a
    /// state with no layout as equal - so the seeded state, which is created
    /// before the first layout exists, swallowed the first rebind after every
    /// launch.
    /// </summary>
    private static bool SameKeys(LayoutResult? a, LayoutResult? b) =>
        a is null
            ? b is null
            : b is not null && LayoutEditor.SameKeyAssignments(a, b);

    /// <summary>
    /// Order is not meaningful - an override set is keyed by slot - so two sets
    /// listing the same overrides differently are the same state.
    /// </summary>
    private static bool Same(IReadOnlyList<DisplayOverride> a, IReadOnlyList<DisplayOverride> b)
    {
        if (a.Count != b.Count) return false;

        var bySlot = b.ToDictionary(o => o.Slot, StringComparer.Ordinal);

        foreach (var one in a)
        {
            if (!bySlot.TryGetValue(one.Slot, out var other)) return false;
            if (one.Columns != other.Columns) return false;
            if (!SameWeights(one.Weights, other.Weights)) return false;
            if (!SameAxes(one.SubzoneAxes, other.SubzoneAxes)) return false;
        }

        return true;
    }

    /// <summary>
    /// Compared by what they MEAN, not by what is written: an absent list, a list
    /// of nulls and a list of words nobody recognises all say "derive it", and
    /// undo has to see those as the same state or it records a step that changes
    /// nothing on screen.
    /// </summary>
    private static bool SameAxes(IReadOnlyList<string?>? a, IReadOnlyList<string?>? b)
    {
        var count = Math.Max(a?.Count ?? 0, b?.Count ?? 0);

        for (var i = 0; i < count; i++)
        {
            if (At(a, i) != At(b, i)) return false;
        }

        return true;

        static Geometry.Axis? At(IReadOnlyList<string?>? list, int i) =>
            list is null || i >= list.Count
                ? null
                : new DisplayOverride("", SubzoneAxes: list).AxisFor(i);
    }

    private static bool SameWeights(IReadOnlyList<double>? a, IReadOnlyList<double>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;

        // Loose, because weights make a round trip through JSON and through the
        // arithmetic of a drag. A difference this small is not an edit.
        return !a.Where((w, i) => Math.Abs(w - b[i]) > 1e-9).Any();
    }
}
