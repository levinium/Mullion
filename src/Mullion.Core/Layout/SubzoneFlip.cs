using Mullion.Core.Geometry;
using Mullion.Core.Model;

namespace Mullion.Core.Layout;

/// <summary>
/// The same subzone, cut the other way.
/// <para>
/// A zone's halves are cut whichever way leaves two usable windows, and that is
/// the right default nearly always and the wrong one occasionally - the desk does
/// not know that the thing being put there is a terminal. So every subzone key
/// also answers with Shift held, and gives the half it would have been had the
/// zone been cut along the other axis.
/// </para>
/// <para>
/// Derived rather than stored, and derived from the RECTANGLES rather than from
/// grid positions. Positions move: a subzone can be rebound to any key on the
/// surface, which breaks any "the key above home" rule. Rectangles do not, and
/// they survive the round trip through a saved profile, which a field on the
/// layout would not - a restored layout has zones and nothing else.
/// </para>
/// </summary>
public static class SubzoneFlip
{
    /// <summary>Slack for the round trip through normalized fractions.</summary>
    private const double Slack = 1e-6;

    /// <summary>
    /// The half <paramref name="zone"/> would be if its parent were cut the other
    /// way, or null when it is not a half of anything.
    /// </summary>
    public static IReadOnlyList<ZonePart>? Of(Zone zone, LayoutResult layout)
    {
        var parent = ParentOf(zone, layout);
        if (parent is null) return null;

        var parts = new List<ZonePart>(zone.Parts.Count);

        foreach (var part in zone.Parts)
        {
            var whole = parent.Parts.FirstOrDefault(p => p.DisplayKey == part.DisplayKey);
            if (whole is null) return null;

            var place = PlaceIn(whole.Area, part.Area);
            if (place is null) return null;

            var (axis, isFirst) = place.Value;
            var other = axis == Axis.Vertical ? Axis.Horizontal : Axis.Vertical;

            parts.Add(new ZonePart(part.DisplayKey, whole.Area.Split(other, [1.0, 1.0])[isFirst ? 0 : 1]));
        }

        return parts;
    }

    /// <summary>
    /// The zone this one is a half of.
    /// <para>
    /// The smallest zone that contains it and is not it. A half is contained by
    /// its parent and by nothing smaller, so this needs no grid arithmetic and
    /// no assumption about which key anything sits on.
    /// </para>
    /// </summary>
    private static Zone? ParentOf(Zone zone, LayoutResult layout)
    {
        Zone? best = null;
        var bestArea = double.MaxValue;

        foreach (var other in layout.Zones)
        {
            if (ReferenceEquals(other, zone) || other.Id == zone.Id) continue;
            if (!Contains(other, zone)) continue;

            var area = AreaOf(other);
            if (area >= bestArea) continue;

            best = other;
            bestArea = area;
        }

        return best;
    }

    private static double AreaOf(Zone zone) => zone.Parts.Sum(p => p.Area.W * p.Area.H);

    /// <summary>
    /// Whether <paramref name="inner"/> sits inside <paramref name="outer"/> on
    /// every display it touches, and is genuinely smaller.
    /// </summary>
    private static bool Contains(Zone outer, Zone inner)
    {
        if (AreaOf(outer) <= AreaOf(inner) + Slack) return false;

        foreach (var part in inner.Parts)
        {
            var host = outer.Parts.FirstOrDefault(p => p.DisplayKey == part.DisplayKey);
            if (host is null) return false;

            if (part.Area.X < host.Area.X - Slack) return false;
            if (part.Area.Y < host.Area.Y - Slack) return false;
            if (part.Area.Right > host.Area.Right + Slack) return false;
            if (part.Area.Bottom > host.Area.Bottom + Slack) return false;
        }

        return true;
    }

    /// <summary>
    /// Which half of its parent a rectangle is, and along which axis. A half
    /// keeping the parent's full width is a stacked one; one keeping its full
    /// height is a side-by-side one.
    /// </summary>
    private static (Axis Axis, bool IsFirst)? PlaceIn(NormRect parent, NormRect half)
    {
        var keepsWidth = Math.Abs(half.W - parent.W) <= Slack;
        var keepsHeight = Math.Abs(half.H - parent.H) <= Slack;

        if (keepsWidth == keepsHeight) return null;

        return keepsWidth
            ? (Axis.Vertical, half.Y <= parent.Y + Slack)
            : (Axis.Horizontal, half.X <= parent.X + Slack);
    }
}
