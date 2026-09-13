using Mullion.Core.Geometry;

namespace Mullion.Core.Layout;

/// <summary>
/// What dropping a window into a zone should do to it.
/// <para>
/// Dropping a window into the zone it already fills used to be a no-op with a
/// flash on the end - the gesture was accepted and nothing happened. It is now
/// the way back: the window returns to the size it had before Mullion filled
/// the zone, and dropping it again fills the zone once more.
/// </para>
/// <para>
/// Here rather than beside the drag handling because it is arithmetic on
/// rectangles and nothing else, and because "does this window fill this zone"
/// has a tolerance in it that wants stating once and testing.
/// </para>
/// </summary>
public static class ZoneFit
{
    /// <summary>
    /// How far from a zone's edges a window may sit and still count as filling
    /// it.
    /// <para>
    /// Not zero: a window that resists being sized exactly - a terminal snapping
    /// to whole character cells, an app with a minimum - lands a few pixels
    /// short, and it would be absurd for the toggle to stop working because a
    /// window was three pixels narrow.
    /// </para>
    /// </summary>
    public const int Tolerance = 6;

    /// <summary>Whether a window is sitting in a zone at that zone's size.</summary>
    public static bool Fills(PxRect window, PxRect zone) =>
        Math.Abs(window.Left - zone.Left) <= Tolerance &&
        Math.Abs(window.Top - zone.Top) <= Tolerance &&
        Math.Abs(window.Right - zone.Right) <= Tolerance &&
        Math.Abs(window.Bottom - zone.Bottom) <= Tolerance;

    /// <summary>
    /// Whether two rectangles are the same size, wherever they happen to be.
    /// <para>
    /// The distinction the size memory turns on. Mullion sets a window's
    /// position and its size together; a user who has since dragged it has
    /// changed only the first, and has chosen no new size to come back to.
    /// </para>
    /// </summary>
    public static bool SameSize(PxRect a, PxRect b) =>
        Math.Abs(a.Width - b.Width) <= Tolerance &&
        Math.Abs(a.Height - b.Height) <= Tolerance;

    /// <summary>
    /// A remembered size, put back inside the zone it is being restored into.
    /// <para>
    /// Centered rather than returned to where it originally was: the window is
    /// being dropped HERE, so here is where it should stay. A window that has
    /// since grown larger than the zone is capped, or restoring it would throw
    /// it outside the zone it was dropped in.
    /// </para>
    /// </summary>
    public static PxRect Restore(PxRect remembered, PxRect zone)
    {
        var width = Math.Min(remembered.Width, zone.Width);
        var height = Math.Min(remembered.Height, zone.Height);

        var left = zone.Left + (zone.Width - width) / 2;
        var top = zone.Top + (zone.Height - height) / 2;

        return PxRect.FromLtrb(left, top, left + width, top + height);
    }

    /// <summary>
    /// What dropping a window into a zone should do, given where it is now and
    /// the last size its owner chose for it.
    /// <para>
    /// Decided here rather than in the drop handler so the probe that verifies
    /// the toggle against a real window is exercising the same rule the app
    /// runs, not a second copy of it that could drift.
    /// </para>
    /// </summary>
    /// <param name="current">Where the window is, or null if it could not be read.</param>
    /// <param name="chosen">The last size not set by Mullion, or null if unknown.</param>
    public static DropPlan Plan(PxRect? current, PxRect? chosen, PxRect zone)
    {
        // Restoring needs all three: the window has to be filling the zone (or
        // there is nothing to come back from), a remembered size has to exist,
        // and it has to differ from the zone - otherwise "restoring" would put
        // the window exactly where it already is and read as a dead gesture.
        var restoring =
            current is { } now && chosen is { } size &&
            Fills(now, zone) && !Fills(size, zone);

        return restoring
            ? new DropPlan(Restore(chosen!.Value, zone), true)
            : new DropPlan(zone, false);
    }
}

/// <summary>Where a dropped window should go, and whether that is the way back.</summary>
public readonly record struct DropPlan(PxRect Target, bool Restoring);
