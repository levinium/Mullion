using Mullion.Core.Geometry;
using Mullion.Core.Model;

namespace Mullion.Core.Layout;

/// <summary>A zone a dragged window can be dropped into, in virtual-desktop pixels.</summary>
public sealed record DropTarget(Zone Zone, PxRect Bounds);

/// <summary>
/// Turns a layout into the set of places a dragged window can be dropped.
/// <para>
/// This is not simply "every zone". Zones deliberately overlap - a column holds
/// the whole of itself AND its upper and lower halves, all bound to different
/// keys - which is fine for a keyboard, where the key says which one you meant,
/// and useless for a pointer, where the cursor sits inside three of them at once
/// and the overlay would have to draw them stacked.
/// </para>
/// <para>
/// So the drop set is the largest tiling the layout allows: take zones biggest
/// first and keep each one that does not overlap something already taken. Where
/// a column has a whole-column zone that wins and its halves drop out; where it
/// has only halves, they tile it between them and both are kept. Either way the
/// result covers the desk without overlapping, so the target under the cursor is
/// never ambiguous.
/// </para>
/// </summary>
public static class DropTargets
{
    public static IReadOnlyList<DropTarget> Build(
        LayoutResult layout, IReadOnlyList<DisplayInfo> displays)
    {
        var byKey = displays.ToDictionary(d => d.StableKey);
        var candidates = new List<DropTarget>();

        foreach (var zone in layout.Zones)
        {
            // A union means "maximize across all of this". It covers the very
            // zones beside it, so as a drop target it would swallow every one
            // of them and the whole desk would become a single target.
            if (zone.Kind == ZoneKind.Union) continue;

            var parts = zone.Parts
                .Where(p => byKey.ContainsKey(p.DisplayKey))
                .Select(p => p.Area.Project(byKey[p.DisplayKey].WorkArea))
                .ToList();

            if (parts.Count == 0) continue;

            var bounds = PxRect.Union(parts);
            if (bounds.IsEmpty) continue;

            candidates.Add(new DropTarget(zone, bounds));
        }

        var accepted = new List<DropTarget>();

        foreach (var candidate in candidates
                     .OrderByDescending(c => c.Bounds.Area)
                     .ThenBy(c => c.Zone.Position.Row)
                     .ThenBy(c => c.Zone.Position.Col))
        {
            if (accepted.Any(a => a.Bounds.Intersects(candidate.Bounds))) continue;
            accepted.Add(candidate);
        }

        return accepted;
    }

    /// <summary>The target under a point in virtual-desktop pixels, or null.</summary>
    public static DropTarget? HitTest(IReadOnlyList<DropTarget> targets, int x, int y)
    {
        foreach (var target in targets)
            if (target.Bounds.Contains(x, y))
                return target;

        return null;
    }
}
