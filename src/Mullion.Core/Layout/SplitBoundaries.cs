namespace Mullion.Core.Layout;

/// <summary>
/// Where a display's zones meet, and what dragging one of those seams does.
/// <para>
/// Weights say how big each slice is; a boundary is the running total between
/// two of them. Dragging is expressed against the boundary rather than the
/// weights because that is the thing on screen - the user grabs the line between
/// two zones, not an abstract proportion, and expects the two zones either side
/// to give and take while nothing further along the display moves at all.
/// </para>
/// </summary>
public static class SplitBoundaries
{
    /// <summary>
    /// The seams between slices, as fractions of the display's long axis.
    /// <para>
    /// One fewer than there are slices: the display's own outer edges are not
    /// boundaries, because there is nothing on the far side of them to trade with.
    /// </para>
    /// </summary>
    public static IReadOnlyList<double> Of(IReadOnlyList<double> weights)
    {
        var total = weights.Sum();
        if (weights.Count < 2 || total <= 0) return [];

        var positions = new List<double>(weights.Count - 1);
        var acc = 0.0;

        for (var i = 0; i < weights.Count - 1; i++)
        {
            acc += weights[i];
            positions.Add(acc / total);
        }

        return positions;
    }

    /// <summary>
    /// Move one seam to <paramref name="position"/>, returning the new weights.
    /// <para>
    /// Only the two slices either side change. Redistributing across all of them
    /// would move seams the user is not touching, which reads as the layout
    /// squirming away from the cursor.
    /// </para>
    /// <para>
    /// <paramref name="minFraction"/> is how small a slice may get - the drag
    /// stops there rather than being rejected, so pushing a seam past its limit
    /// parks it at the limit instead of snapping back to where it started.
    /// </para>
    /// </summary>
    public static IReadOnlyList<double> Move(
        IReadOnlyList<double> weights, int index, double position, double minFraction)
    {
        var total = weights.Sum();
        if (index < 0 || index >= weights.Count - 1 || total <= 0) return weights;

        var normalized = weights.Select(w => w / total).ToArray();

        // Everything before this seam is fixed, and so is everything after the
        // slice on its far side. The pair between them shares a fixed span.
        var start = normalized.Take(index).Sum();
        var span = normalized[index] + normalized[index + 1];

        // A slice cannot be smaller than the floor, and a pair of them cannot
        // between them exceed the span they share.
        var floor = Math.Min(minFraction, span / 2);
        var clamped = Math.Clamp(position, start + floor, start + span - floor);

        var updated = (double[])normalized.Clone();
        updated[index] = clamped - start;
        updated[index + 1] = span - updated[index];

        return updated;
    }
}
