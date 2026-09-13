using Mullion.Core.Geometry;

namespace Mullion.Core.Layout;

/// <summary>
/// The last size a window had that Mullion did not give it.
/// <para>
/// So that filling a zone can be undone by filling it again: the window goes
/// back to whatever size its owner last chose for it, not to some earlier zone
/// Mullion happened to put it in.
/// </para>
/// <para>
/// Kept by watching, not by asking. Before every move, the window's current
/// size is compared with what Mullion last set: if they differ, the window has
/// been moved or resized by hand since, and THAT is the size worth remembering.
/// If they match, nothing has happened that we did not do, and the older memory
/// still stands.
/// </para>
/// </summary>
public sealed class WindowSizeMemory
{
    /// <summary>
    /// How many windows to remember. Generous - the cost is a rectangle each -
    /// but not unbounded, because window handles are recycled and a process
    /// that runs for weeks would otherwise accumulate one entry per window
    /// anything ever opened.
    /// </summary>
    private const int Capacity = 64;

    private readonly Dictionary<nint, Entry> _entries = [];
    private readonly List<nint> _order = [];

    private sealed record Entry(PxRect Chosen, PxRect? Applied);

    /// <summary>
    /// Note where a window is before Mullion moves it, and answer with the size
    /// to put it back to.
    /// </summary>
    public PxRect? Observe(nint window, PxRect current)
    {
        var known = _entries.TryGetValue(window, out var entry);

        // Still the size we last set it to: whatever we were remembering before
        // is still the size its owner chose.
        //
        // Size, not position. Dragging a window is the gesture that carries the
        // toggle, and it moves the window without resizing it - so a position
        // test would read every drag as the user picking a new size, and the
        // size picked would be the zone's own.
        if (known && entry!.Applied is { } applied && ZoneFit.SameSize(current, applied))
            return entry.Chosen;

        Put(window, new Entry(current, known ? entry!.Applied : null));

        return current;
    }

    /// <summary>Record what Mullion just set, so the next move can tell it apart.</summary>
    public void Applied(nint window, PxRect rect)
    {
        var chosen = _entries.TryGetValue(window, out var entry) ? entry.Chosen : rect;

        Put(window, new Entry(chosen, rect));
    }

    /// <summary>The size to toggle back to, or null for a window never seen.</summary>
    public PxRect? ChosenSizeOf(nint window) =>
        _entries.TryGetValue(window, out var entry) ? entry.Chosen : null;

    public void Forget(nint window)
    {
        _entries.Remove(window);
        _order.Remove(window);
    }

    public int Count => _entries.Count;

    private void Put(nint window, Entry entry)
    {
        if (!_entries.ContainsKey(window)) _order.Add(window);

        _entries[window] = entry;

        // Oldest first: a handle not touched in the last sixty-odd moves is far
        // likelier to belong to a window that has closed than to one someone is
        // still arranging.
        while (_order.Count > Capacity)
        {
            var oldest = _order[0];
            _order.RemoveAt(0);
            _entries.Remove(oldest);
        }
    }
}
