namespace Mullion.Core.Layout;

/// <summary>
/// Undo and redo over a display's zone customizations.
/// <para>
/// Snapshots of the whole override set rather than a log of individual edits.
/// An edit here is not independent of its neighbours - dragging a seam changes
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

    private readonly List<IReadOnlyList<DisplayOverride>> _past = [];
    private readonly List<IReadOnlyList<DisplayOverride>> _future = [];

    private IReadOnlyList<DisplayOverride> _present;

    public LayoutHistory(IReadOnlyList<DisplayOverride>? initial = null) =>
        _present = initial is null ? [] : [.. initial];

    public bool CanUndo => _past.Count > 0;

    public bool CanRedo => _future.Count > 0;

    /// <summary>The state as it now stands.</summary>
    public IReadOnlyList<DisplayOverride> Present => _present;

    /// <summary>
    /// Take note of an edit that has just happened.
    /// <para>
    /// An edit that changes nothing is not recorded. Dragging a seam reports on
    /// release even when the pointer never moved, and a click that did nothing
    /// should not cost an undo press to get past.
    /// </para>
    /// </summary>
    public void Record(IReadOnlyList<DisplayOverride> state)
    {
        if (Same(state, _present)) return;

        _past.Add(_present);
        if (_past.Count > Depth) _past.RemoveAt(0);

        // A new edit abandons the branch that was undone away from. Keeping it
        // would let redo jump to a state that no longer follows from this one.
        _future.Clear();

        _present = [.. state];
    }

    public IReadOnlyList<DisplayOverride>? Undo()
    {
        if (_past.Count == 0) return null;

        _future.Add(_present);
        _present = _past[^1];
        _past.RemoveAt(_past.Count - 1);

        return _present;
    }

    public IReadOnlyList<DisplayOverride>? Redo()
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
    public void Reset(IReadOnlyList<DisplayOverride> state)
    {
        _past.Clear();
        _future.Clear();
        _present = [.. state];
    }

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
        }

        return true;
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
