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
        bool allowSpanningUnions = true,
        IReadOnlyList<DisplayOverride>? overrides = null)
    {
        var s = surface ?? KeySurface.LeftHandBlock;
        var t = tuning ?? ShapeTuning.Default;
        var notes = new List<string>();

        // Keyed by slot, so an override follows the place on the desk rather
        // than the monitor that happened to be there when it was made.
        var custom = (overrides ?? [])
            .Where(o => !o.IsEmpty)
            .GroupBy(o => o.Slot)
            .ToDictionary(g => g.Key, g => g.Last());

        if (displays.Count == 0) return new LayoutResult([], s, ["No displays detected."]);

        var columns = DisplayGrid.Columns(displays);
        var hasOthers = displays.Count > 1;

        // --- Step 1: horizontal demand per display column -------------------
        // A landscape display subdivides into columns; a portrait one subdivides
        // into rows and therefore asks for a single column.
        var demand = columns
            .Select(c => c.Displays.Max(d => HorizontalSplits(d, hasOthers, t, custom)))
            .ToArray();

        // A hand-set count is a floor as well as a demand: coarsening exists to
        // fit the key surface, and silently undoing what somebody chose is not
        // the way to find the room.
        var floor = columns
            .Select(c => c.Displays.Max(d => HorizontalFloor(d, hasOthers, t, custom)))
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
        // Exactly two zones across get the OUTER keys - A and D on QWERTY -
        // rather than packing from the left. That is the left/right mnemonic the
        // two-monitor default is built on, and it leaves the home key between
        // them free for the span. Three or more pack with no gap, so the home
        // row keeps meaning "the whole of this column" everywhere.
        var spread = Granted(demand, s.Cols) == 2 && s.Cols >= 3;

        var zones = new List<Zone>();
        var surfaceCol = 0;

        for (var ci = 0; ci < columns.Count && surfaceCol < s.Cols; ci++)
        {
            var column = columns[ci];
            var splits = Math.Max(1, Math.Min(demand[ci], s.Cols - surfaceCol));

            for (var slice = 0; slice < splits; slice++, surfaceCol++)
            {
                var target = spread && surfaceCol == 1 ? 2 : surfaceCol;
                zones.AddRange(BuildSubColumn(
                    column, slice, splits, target, s, t, hasOthers, allowSpanningUnions, custom, notes));
            }
        }

        // --- Step 4: the key left sitting between them -----------------------
        if (spread) zones.AddRange(BuildSpanZone(zones, displays, s, allowSpanningUnions, notes));

        return new LayoutResult(zones, s, notes);
    }

    /// <summary>How many surface columns the allocation actually consumes.</summary>
    private static int Granted(IReadOnlyList<int> demand, int cols)
    {
        var used = 0;
        foreach (var d in demand)
        {
            if (used >= cols) break;
            used += Math.Max(1, Math.Min(d, cols - used));
        }

        return used;
    }

    /// <summary>
    /// Bind the key between two flanking zones to their span. Inside one display
    /// that just means "maximize here" and is always worth having; across two it
    /// is a bezel-crossing zone, so it needs the same clean-rectangle guard a
    /// stacked pair does - otherwise two ordinary monitors silently gain a span
    /// nobody asked for, and mixed DPI gives a window that renders wrong on one.
    /// </summary>
    private static IEnumerable<Zone> BuildSpanZone(
        List<Zone> zones,
        IReadOnlyList<DisplayInfo> displays,
        KeySurface s,
        bool allowSpanningUnions,
        List<string> notes)
    {
        var home = s.HomeRow;
        var left = zones.FirstOrDefault(z => z.Position == new GridPos(home, 0));
        var right = zones.FirstOrDefault(z => z.Position == new GridPos(home, 2));

        // A ragged stack can leave a column's home key unbound; there is then no
        // pair to span.
        if (left is null || right is null) yield break;

        var keys = left.Parts.Concat(right.Parts).Select(p => p.DisplayKey).Distinct().ToList();

        if (keys.Count > 1)
        {
            var involved = displays.Where(d => keys.Contains(d.StableKey)).ToList();
            if (!allowSpanningUnions) yield break;

            if (!DisplayGrid.FormsCleanRectangle(involved))
            {
                notes.Add(
                    "The displays either side do not form a clean rectangle " +
                    "(mismatched size, a gap, or differing scaling), so the key between them is unbound.");
                yield break;
            }
        }

        var parts = left.Parts.Concat(right.Parts)
            .GroupBy(p => p.DisplayKey)
            .Select(g => new ZonePart(g.Key, Cover([.. g.Select(p => p.Area)])))
            .ToList();

        // The rows above and below the span key would otherwise sit idle, while
        // every other column already spends its spare rows on the upper and
        // lower halves of what its home key holds. This makes the span column
        // the same as the rest rather than a special case.
        foreach (var (row, index, name) in new[]
                 {
                     (home - 1, 0, "Upper half"),
                     (home + 1, 1, "Lower half"),
                 })
        {
            if (!s.Contains(new GridPos(row, 1))) continue;

            yield return new Zone
            {
                Id = Guid.NewGuid(),
                Name = name,
                Parts = [.. parts.Select(p =>
                    new ZonePart(p.DisplayKey, p.Area.Split(Axis.Vertical, [1, 1])[index]))],
                Position = new GridPos(row, 1),
                Kind = ZoneKind.Union,
            };
        }

        yield return new Zone
        {
            Id = Guid.NewGuid(),
            Name = keys.Count > 1 ? "Both displays" : "Whole display",
            Parts = parts,
            Position = new GridPos(home, 1),
            Kind = ZoneKind.Union,
        };
    }

    /// <summary>The smallest rectangle containing them all.</summary>
    private static NormRect Cover(IReadOnlyList<NormRect> areas)
    {
        var x = areas.Min(a => a.X);
        var y = areas.Min(a => a.Y);
        return new NormRect(x, y, areas.Max(a => a.Right) - x, areas.Max(a => a.Bottom) - y);
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
        IReadOnlyDictionary<string, DisplayOverride> custom,
        List<string> notes)
    {
        var depth = column.Depth;
        var home = s.HomeRow;

        // Each display's horizontal slice, top to bottom.
        var slices = column.Displays
            .Select(d => (Display: d, Area: HorizontalSlice(d, slice, sliceCount, hasOthers, t, custom)))
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
                        Name = VerticalLabel(r, s.Rows),
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
            // Position only. With no split there is no positional word, so the
            // display name is the only sensible label for the whole thing.
            var baseName = sliceCount > 1
                ? HorizontalLabel(slice, sliceCount)
                : display.FriendlyName;

            yield return new Zone
            {
                Id = Guid.NewGuid(),
                Name = baseName,
                Parts = [new ZonePart(display.StableKey, area)],
                Position = new GridPos(home, surfaceCol),
                Kind = sliceCount > 1 ? ZoneKind.Region : ZoneKind.WholeDisplay,
            };

            // Cut whichever way leaves two usable windows. Stacking was
            // unconditional, which on anything wide produced a pair the engine
            // would have rejected outright as zones - halving a 16:9 gives two
            // 1920x540 letterboxes against a ZoneAspectMax of 2.20.
            //
            // A hand-set axis wins, because which way to cut is a preference as
            // much as a measurement: a zone can be the right shape for
            // side-by-side halves and still be the place someone always wants one
            // window above another.
            var tierAxis =
                CustomAxis(display, slice, custom)
                ?? TierAxis.For(area, display.WorkArea, display.Dpi, t);
            var halves = area.Split(tierAxis, [1.0, 1.0]);

            // The key above home always takes the first half, the key below the
            // second - top before bottom, left before right. The words follow the
            // axis so the name and the rectangle can never disagree.
            var (firstWord, secondWord) = tierAxis == Axis.Vertical
                ? ("upper", "lower")
                : ("left", "right");

            if (home - 1 >= 0)
            {
                yield return new Zone
                {
                    Id = Guid.NewGuid(),
                    Name = $"{baseName} {firstWord}",
                    Parts = [new ZonePart(display.StableKey, halves[0])],
                    Position = new GridPos(home - 1, surfaceCol),
                };
            }

            if (home + 1 < s.Rows)
            {
                yield return new Zone
                {
                    Id = Guid.NewGuid(),
                    Name = $"{baseName} {secondWord}",
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
                    ? HorizontalLabel(slice, sliceCount)
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
        DisplayInfo display, int slice, int sliceCount, bool hasOthers, ShapeTuning t,
        IReadOnlyDictionary<string, DisplayOverride> custom)
    {
        if (sliceCount <= 1) return NormRect.Full;

        var weights = CustomWeights(display, sliceCount, custom)
                      ?? SplitGenerator.Best(display.Bounds, sliceCount, t).Weights;

        return NormRect.Full.Split(Axis.Horizontal, weights)[slice];
    }

    /// <summary>
    /// Hand-set weights, but only when they still describe this many zones: a
    /// count changed since they were saved leaves them meaningless, and half a
    /// remembered split is worse than a freshly derived one.
    /// </summary>
    private static IReadOnlyList<double>? CustomWeights(
        DisplayInfo display, int count, IReadOnlyDictionary<string, DisplayOverride> custom)
    {
        if (!custom.TryGetValue(DisplaySlot.Of(display), out var o)) return null;
        if (o.Weights is null || o.Weights.Count != count) return null;

        return o.Weights.All(w => w > 0) ? o.Weights : null;
    }

    /// <summary>
    /// The subzone axis someone chose for one zone of this display, or null to
    /// derive it from the zone's shape.
    /// </summary>
    private static Axis? CustomAxis(
        DisplayInfo display, int zone, IReadOnlyDictionary<string, DisplayOverride> custom) =>
        custom.TryGetValue(DisplaySlot.Of(display), out var o) ? o.AxisFor(zone) : null;

    private static int HorizontalSplits(
        DisplayInfo d, bool hasOthers, ShapeTuning t,
        IReadOnlyDictionary<string, DisplayOverride> custom)
    {
        if (d.SplitAxis != Axis.Horizontal) return 1;

        if (custom.TryGetValue(DisplaySlot.Of(d), out var o) && o.Columns is > 0)
            return o.Columns.Value;

        return ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, hasOthers, t).Preferred;
    }

    private static int HorizontalFloor(
        DisplayInfo d, bool hasOthers, ShapeTuning t,
        IReadOnlyDictionary<string, DisplayOverride> custom)
    {
        if (d.SplitAxis != Axis.Horizontal) return 1;

        if (custom.TryGetValue(DisplaySlot.Of(d), out var o) && o.Columns is > 0)
            return o.Columns.Value;

        return ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, hasOthers, t).Min;
    }

    private static int VerticalSplits(DisplayInfo d, bool hasOthers, ShapeTuning t) =>
        d.SplitAxis == Axis.Vertical
            ? ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, hasOthers, t).Preferred
            : 1;

    // Zone names carry POSITION only, not the monitor. The diagram labels each
    // display directly above it, so repeating the name in every zone was noise -
    // "C49RG9x left" says nothing "Left" does not. Where a list needs to
    // disambiguate between monitors it composes the two itself.
    private static string HorizontalLabel(int index, int count) => count switch
    {
        2 => index == 0 ? "Left" : "Right",
        3 => index switch { 0 => "Left", 1 => "Center", _ => "Right" },
        4 => index switch { 0 => "Far left", 1 => "Left", 2 => "Right", _ => "Far right" },
        _ => $"Column {index + 1}",
    };

    private static string VerticalLabel(int index, int count) => count switch
    {
        2 => index == 0 ? "Upper" : "Lower",
        3 => index switch { 0 => "Upper", 1 => "Middle", _ => "Lower" },
        _ => $"Row {index + 1}",
    };
}
