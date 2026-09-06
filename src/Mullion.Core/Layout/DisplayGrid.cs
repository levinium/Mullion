using Mullion.Core.Geometry;
using Mullion.Core.Model;

namespace Mullion.Core.Layout;

/// <summary>A vertical slice of the desk, holding the displays stacked within it.</summary>
/// <param name="Index">Left-to-right position.</param>
/// <param name="Displays">Top-to-bottom within this column.</param>
public sealed record DisplayColumn(int Index, IReadOnlyList<DisplayInfo> Displays)
{
    public int Depth => Displays.Count;
    public PxRect Bounds => PxRect.Union(Displays.Select(d => d.Bounds));
}

/// <summary>
/// Groups displays into columns, then analyses stacking WITHIN each column.
/// <para>
/// Per-column analysis is load-bearing. Clustering bands globally with union-find
/// over displays cannot represent a desk containing a tall display: two verticals
/// flanking a stacked pair each overlap vertically with BOTH center monitors, so
/// the transitive merge collapses all four into a single band and the arrangement
/// becomes unrepresentable. Columns have no such failure, because a column's
/// stacking is a local property.
/// </para>
/// </summary>
public static class DisplayGrid
{
    /// <summary>
    /// Two displays share a column when they overlap horizontally by more than
    /// half the narrower one's width.
    /// </summary>
    public static IReadOnlyList<DisplayColumn> Columns(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) return [];

        var uf = new UnionFind(displays.Count);

        for (var i = 0; i < displays.Count; i++)
        for (var j = i + 1; j < displays.Count; j++)
        {
            var a = displays[i].Bounds;
            var b = displays[j].Bounds;
            var overlap = a.HorizontalOverlap(b);
            var narrower = Math.Min(a.Width, b.Width);

            if (narrower > 0 && overlap * 2 > narrower) uf.Union(i, j);
        }

        return [.. displays
            .Select((d, i) => (Display: d, Root: uf.Find(i)))
            .GroupBy(x => x.Root)
            // Order columns by their left edge, so index 0 is genuinely leftmost
            // even when a display sits at a negative X.
            .OrderBy(g => g.Min(x => x.Display.Bounds.Left))
            .ThenBy(g => g.Min(x => x.Display.Bounds.Top))
            .Select((g, index) => new DisplayColumn(
                index,
                [.. g.Select(x => x.Display)
                     .OrderBy(d => d.Bounds.Top)
                     .ThenBy(d => d.Bounds.Left)]))];
    }

    /// <summary>
    /// Rows needed by the arrangement itself: the deepest column's stack.
    /// The surface may offer more, and the surplus is spent on tiers.
    /// </summary>
    public static int RequiredRows(IReadOnlyList<DisplayColumn> columns) =>
        columns.Count == 0 ? 0 : columns.Max(c => c.Depth);

    /// <summary>
    /// Whether two displays form a clean rectangle, which is the condition for
    /// binding a union key that spans them.
    /// </summary>
    /// <remarks>
    /// Without this guard the union rule misfires on the most ordinary setups:
    /// two monitors side by side would silently bind a bezel-crossing zone
    /// nobody asked for. Differing DPI is disqualifying because a window
    /// spanning two scaling factors takes the DPI of whichever monitor holds
    /// its majority and renders wrong on the other.
    /// </remarks>
    public static bool FormsCleanRectangle(DisplayInfo a, DisplayInfo b, int tolerancePx = 2)
    {
        if (a.Dpi != b.Dpi) return false;

        var ra = a.Bounds;
        var rb = b.Bounds;

        var verticallyStacked =
            Math.Abs(ra.Left - rb.Left) <= tolerancePx &&
            Math.Abs(ra.Right - rb.Right) <= tolerancePx &&
            (Math.Abs(ra.Bottom - rb.Top) <= tolerancePx || Math.Abs(rb.Bottom - ra.Top) <= tolerancePx);

        var horizontallyAdjacent =
            Math.Abs(ra.Top - rb.Top) <= tolerancePx &&
            Math.Abs(ra.Bottom - rb.Bottom) <= tolerancePx &&
            (Math.Abs(ra.Right - rb.Left) <= tolerancePx || Math.Abs(rb.Right - ra.Left) <= tolerancePx);

        return verticallyStacked || horizontallyAdjacent;
    }

    /// <summary>All displays in the column form one clean rectangle, pairwise up the stack.</summary>
    public static bool FormsCleanRectangle(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count <= 1) return true;

        for (var i = 0; i < displays.Count - 1; i++)
            if (!FormsCleanRectangle(displays[i], displays[i + 1]))
                return false;

        return true;
    }

    private sealed class UnionFind(int count)
    {
        private readonly int[] _parent = [.. Enumerable.Range(0, count)];

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }

            return x;
        }

        public void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) _parent[rb] = ra;
        }
    }
}
