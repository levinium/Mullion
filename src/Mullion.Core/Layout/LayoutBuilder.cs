using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Model;

namespace Mullion.Core.Layout;

/// <summary>The generated layout plus what had to be given up to make it fit.</summary>
public sealed record LayoutResult(
    IReadOnlyList<Zone> Zones,
    KeySurface Surface,
    IReadOnlyList<string> Notes)
{
    public Zone? At(GridPos p) => Zones.FirstOrDefault(z => z.Position == p);
    public Zone? At(int row, int col) => At(new GridPos(row, col));
}

/// <summary>
/// Maps a display arrangement onto a key surface.
/// <para>
/// The surface is treated as a BUDGET, not merely a ceiling. Under-spending is
/// as much a failure as overflowing: three vertical monitors bound to just three
/// keys leaves ten idle while every window is stuck full-height. So surplus rows
/// are spent on tiers, and scarcity is paid by coarsening in a defined order.
/// </para>
/// <para>
/// The invariant that makes this safe is HOME-ROW INVARIANCE: whatever else
/// changes, the home row always means "the whole of this column". Tiers are
/// strictly additive, so nothing a user has already learned ever moves.
/// </para>
/// </summary>
public static class LayoutBuilder
{
    public static LayoutResult Build(
        IReadOnlyList<DisplayInfo> displays,
        KeySurface? surface = null,
        ShapeTuning? tuning = null,
        bool allowSpanningUnions = true)
    {
        var s = surface ?? KeySurface.LeftHandBlock;
        var t = tuning ?? ShapeTuning.Default;
        var notes = new List<string>();

        if (displays.Count == 0) return new LayoutResult([], s, ["No displays detected."]);

        var columns = DisplayGrid.Columns(displays);
        var hasOthers = displays.Count > 1;

        // --- Step 1: horizontal demand per display column -------------------
        // A landscape display subdivides into columns; a portrait one subdivides
        // into rows and therefore asks for a single column.
        var demand = columns
            .Select(c => c.Displays.Max(d => HorizontalSplits(d, hasOthers, t)))
            .ToArray();

        var floor = columns
            .Select(c => c.Displays.Max(d => HorizontalFloor(d, hasOthers, t)))
            .ToArray();

        // --- Step 2: coarsen if over budget ---------------------------------
        // Uniform division (cols / columnCount) would be wrong: a 16:9 beside a
        // 32:9 would give both 2, halving the 16:9 that should stay whole and
        // under-splitting the 32:9 that wants 3. Allocate by demand instead.
        while (demand.Sum() > s.Cols)
        {
            var victim = -1;
            var slack = 0;
            for (var i = 0; i < demand.Length; i++)
            {
                if (demand[i] - floor[i] > slack)
                {
                    slack = demand[i] - floor[i];
                    victim = i;
                }
            }

            if (victim < 0)
            {
                notes.Add(
                    $"This arrangement needs {demand.Sum()} columns but the {s.Name} surface has {s.Cols}. " +
                    "Some displays share a key - assign more in Settings.");
                break;
            }

            demand[victim]--;
            notes.Add($"Reduced column {victim + 1} to {demand[victim]} zones to fit the key surface.");
        }

        // --- Step 3: assign surface columns, then build each sub-column ------
        var zones = new List<Zone>();
        var surfaceCol = 0;

        for (var ci = 0; ci < columns.Count && surfaceCol < s.Cols; ci++)
        {
            var column = columns[ci];
            var splits = Math.Max(1, Math.Min(demand[ci], s.Cols - surfaceCol));

            for (var slice = 0; slice < splits; slice++, surfaceCol++)
                zones.AddRange(BuildSubColumn(column, slice, splits, surfaceCol, s, t, hasOthers, allowSpanningUnions, notes));
        }

        return new LayoutResult(zones, s, notes);
    }

    /// <summary>
    /// Build the vertical structure of one surface column: a horizontal slice
    /// taken through every display stacked in this display column.
    /// </summary>
    private static IEnumerable<Zone> BuildSubColumn(
        DisplayColumn column,
        int slice,
        int sliceCount,
        int surfaceCol,
        KeySurface s,
        ShapeTuning t,
        bool hasOthers,
        bool allowSpanningUnions,
        List<string> notes)
    {
        var depth = column.Depth;
        var home = s.HomeRow;

        // Each display's horizontal slice, top to bottom.
        var slices = column.Displays
            .Select(d => (Display: d, Area: HorizontalSlice(d, slice, sliceCount, hasOthers, t)))
            .ToList();

        // ---- One display in this column: spend spare rows on tiers ---------
        if (depth == 1)
        {
            var (display, area) = slices[0];
            var verticalSplits = VerticalSplits(display, hasOthers, t);

            // A portrait display wanting as many rows as the surface has takes
            // them all; there is no room left for a whole-display key, and none
            // is needed since the stacked zones already tile it.
            if (verticalSplits >= s.Rows)
            {
                var parts = area.Split(Axis.Vertical, SplitGenerator.Best(display.Bounds, s.Rows, t).Weights);
                for (var r = 0; r < s.Rows; r++)
                {
                    yield return new Zone
                    {
                        Id = Guid.NewGuid(),
                        Name = $"{display.FriendlyName} {VerticalLabel(r, s.Rows)}",
                        Parts = [new ZonePart(display.StableKey, parts[r])],
                        Position = new GridPos(r, surfaceCol),
                        Kind = ZoneKind.Region,
                    };
                }

                yield break;
            }

            // Otherwise: home row is the whole slice, rows above and below are
            // its upper and lower halves. Additive - the home row is untouched.
            //
            // Tier names must be built from the SLICE, not the display: on a
            // three-way-split monitor, naming them all after the display gives
            // three zones called "Display upper" and the wizard cannot tell them
            // apart.
            var baseName = sliceCount > 1
                ? $"{display.FriendlyName} {HorizontalLabel(slice, sliceCount)}"
                : display.FriendlyName;

            yield return new Zone
            {
                Id = Guid.NewGuid(),
                Name = baseName,
                Parts = [new ZonePart(display.StableKey, area)],
                Position = new GridPos(home, surfaceCol),
                Kind = sliceCount > 1 ? ZoneKind.Region : ZoneKind.WholeDisplay,
            };

            var halves = area.Split(Axis.Vertical, [1.0, 1.0]);

            if (home - 1 >= 0)
            {
                yield return new Zone
                {
                    Id = Guid.NewGuid(),
                    Name = $"{baseName} upper",
                    Parts = [new ZonePart(display.StableKey, halves[0])],
                    Position = new GridPos(home - 1, surfaceCol),
                };
            }

            if (home + 1 < s.Rows)
            {
                yield return new Zone
                {
                    Id = Guid.NewGuid(),
                    Name = $"{baseName} lower",
                    Parts = [new ZonePart(display.StableKey, halves[1])],
                    Position = new GridPos(home + 1, surfaceCol),
                };
            }

            yield break;
        }

        // ---- Several displays stacked: they own the outer rows -------------
        // With depth 2 and 3 rows the displays take rows 0 and 2, leaving the
        // home row for the union - so the row meaning stays "upper / whole /
        // lower" in every column, matching the columns that hold one display.
        var rows = RowsForDepth(depth, s.Rows);

        for (var i = 0; i < depth && i < rows.Count; i++)
        {
            var (display, area) = slices[i];
            yield return new Zone
            {
                Id = Guid.NewGuid(),
                Name = sliceCount > 1
                    ? $"{display.FriendlyName} {HorizontalLabel(slice, sliceCount)}"
                    : display.FriendlyName,
                Parts = [new ZonePart(display.StableKey, area)],
                Position = new GridPos(rows[i], surfaceCol),
                Kind = sliceCount > 1 ? ZoneKind.Region : ZoneKind.WholeDisplay,
            };
        }

        // The union key, only when the displays form a clean rectangle. Without
        // that guard the rule misfires: two ordinary monitors would silently
        // gain a bezel-crossing zone nobody asked for, and mixed DPI would give
        // a window that renders wrong on one of the two.
        if (rows.Contains(home)) yield break;

        var clean = DisplayGrid.FormsCleanRectangle(column.Displays);
        if (!allowSpanningUnions || !clean)
        {
            if (!clean)
            {
                notes.Add(
                    $"Column {surfaceCol + 1}: displays do not form a clean rectangle " +
                    "(mismatched size, a gap, or differing scaling), so the whole-column key is unbound.");
            }

            yield break;
        }

        yield return new Zone
        {
            Id = Guid.NewGuid(),
            Name = string.Join(" + ", column.Displays.Select(d => d.FriendlyName)),
            Parts = [.. slices.Select(x => new ZonePart(x.Display.StableKey, x.Area))],
            Position = new GridPos(home, surfaceCol),
            Kind = ZoneKind.Union,
        };
    }

    /// <summary>Which surface rows a stack of <paramref name="depth"/> displays occupies.</summary>
    private static IReadOnlyList<int> RowsForDepth(int depth, int surfaceRows)
    {
        if (depth >= surfaceRows) return [.. Enumerable.Range(0, surfaceRows)];

        // Depth 2 on a 3-row surface: outermost rows, leaving the home row free
        // for the union so row meanings stay consistent across columns.
        if (depth == 2 && surfaceRows == 3) return [0, 2];

        return [.. Enumerable.Range(0, depth)];
    }

    private static NormRect HorizontalSlice(
        DisplayInfo display, int slice, int sliceCount, bool hasOthers, ShapeTuning t)
    {
        if (sliceCount <= 1) return NormRect.Full;

        var weights = SplitGenerator.Best(display.Bounds, sliceCount, t).Weights;
        return NormRect.Full.Split(Axis.Horizontal, weights)[slice];
    }

    private static int HorizontalSplits(DisplayInfo d, bool hasOthers, ShapeTuning t) =>
        d.SplitAxis == Axis.Horizontal
            ? ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, hasOthers, t).Preferred
            : 1;

    private static int HorizontalFloor(DisplayInfo d, bool hasOthers, ShapeTuning t) =>
        d.SplitAxis == Axis.Horizontal
            ? ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, hasOthers, t).Min
            : 1;

    private static int VerticalSplits(DisplayInfo d, bool hasOthers, ShapeTuning t) =>
        d.SplitAxis == Axis.Vertical
            ? ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, hasOthers, t).Preferred
            : 1;

    private static string HorizontalLabel(int index, int count) => count switch
    {
        2 => index == 0 ? "left" : "right",
        3 => index switch { 0 => "left", 1 => "centre", _ => "right" },
        _ => $"column {index + 1}",
    };

    private static string VerticalLabel(int index, int count) => count switch
    {
        2 => index == 0 ? "upper" : "lower",
        3 => index switch { 0 => "upper", 1 => "middle", _ => "lower" },
        _ => $"row {index + 1}",
    };
}
