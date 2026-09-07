using Mullion.Core.Geometry;

namespace Mullion.Core.Layout;

/// <summary>
/// The positions a dragged seam should prefer to land on.
/// <para>
/// A plain percentage grid would do the obvious job, but it misses the positions
/// that actually matter on a given display. The layout engine already scores a
/// split higher when one pane comes out at an exact 16:9 or 3:2 - that is why a
/// 32:9 defaults to 25/50/25 rather than equal thirds - and those positions
/// depend on the monitor's own proportions, so no fixed grid can contain them.
/// A seam dragged by hand should be able to reach them.
/// </para>
/// <para>
/// So the candidates are the grid, the simple divisions, AND the exact-aspect
/// positions for this display, and a drag lands on whichever is nearest.
/// </para>
/// </summary>
public static class SplitSnapping
{
    /// <summary>Coarse enough to be easy to hit, fine enough not to feel restrictive.</summary>
    public const double DefaultStep = 0.05;

    /// <summary>Where a seam between two zones may land, as fractions of the display.</summary>
    /// <param name="spanStart">Fixed near edge of the two zones being resized.</param>
    /// <param name="spanEnd">Their fixed far edge.</param>
    /// <param name="alongPx">Work-area extent along the split axis.</param>
    /// <param name="acrossPx">Its extent across - what an aspect ratio is measured against.</param>
    public static IReadOnlyList<double> Candidates(
        double spanStart,
        double spanEnd,
        int alongPx,
        int acrossPx,
        double step = DefaultStep,
        ShapeTuning? tuning = null)
    {
        var found = new List<double>();

        if (spanEnd <= spanStart || alongPx <= 0 || acrossPx <= 0) return found;

        void Offer(double at)
        {
            if (at > spanStart && at < spanEnd) found.Add(at);
        }

        // The grid, across the whole display so seams on the same monitor line
        // up with each other rather than each having its own origin. Counted
        // out rather than accumulated: adding 0.05 twenty times does not reach
        // 1.0, and the drift shows up as a grid that is not quite square.
        if (step > 0)
            for (var i = 1; i * step < 1.0; i++)
                Offer(i * step);

        // Halves and thirds of the pair. A half is already on a 5% grid; a third
        // never is, and one-third-two-thirds is a layout people want.
        var span = spanEnd - spanStart;
        Offer(spanStart + span / 2);
        Offer(spanStart + span / 3);
        Offer(spanStart + span * 2 / 3);

        // A pane of exactly this ratio, measured from each end of the pair. The
        // reason a 2560px zone on a 5120x1440 is worth landing on precisely.
        foreach (var canonical in (tuning ?? ShapeTuning.Default).CanonicalRatios)
        {
            var exact = acrossPx * canonical.Ratio / alongPx;

            Offer(spanStart + exact);
            Offer(spanEnd - exact);
        }

        return [.. found.Distinct().Order()];
    }

    /// <summary>
    /// The nearest candidate to <paramref name="position"/>, or the position
    /// itself when there is nothing to snap to.
    /// </summary>
    public static double Snap(double position, IReadOnlyList<double> candidates)
    {
        if (candidates.Count == 0) return position;

        var best = candidates[0];
        var distance = Math.Abs(position - best);

        foreach (var candidate in candidates)
        {
            var away = Math.Abs(position - candidate);
            if (away >= distance) continue;

            best = candidate;
            distance = away;
        }

        return best;
    }

    /// <summary>
    /// Work-area extents along and across a display's split axis.
    /// <para>
    /// The work area rather than the bounds, because that is what the zones
    /// divide: a pane sized against the full height would be short by the
    /// taskbar and not the ratio it claims to be.
    /// </para>
    /// </summary>
    public static (int Along, int Across) Extents(PxRect workArea, bool horizontal) =>
        horizontal
            ? (workArea.Width, workArea.Height)
            : (workArea.Height, workArea.Width);
}
