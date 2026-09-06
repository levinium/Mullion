using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Controls;
using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;

namespace Mullion.App.ViewModels;

/// <summary>
/// One drawable cell of a display.
/// <para>
/// A cell is not simply a zone. Where the key surface has rows to spare, a
/// column holds a full-height zone AND its upper and lower halves - three zones
/// occupying overlapping space. Drawing all three as rectangles stacks them and
/// the labels collide. So the tallest zone becomes the cell's rectangle and the
/// tiers are drawn as chips pinned to its top and bottom edges, which states
/// "A is the whole column, Q its upper half, Z its lower half" without overlap.
/// </para>
/// </summary>
public sealed partial class ZoneCellViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCapturing;

    public required string Name { get; init; }
    public required string KeyLabel { get; init; }
    public required Rect Area { get; init; }
    public required string SizeLabel { get; init; }
    public required bool SpansDisplays { get; init; }

    /// <summary>Where this cell sits on the key surface, so a click can rebind it.</summary>
    public GridPos Position { get; init; }

    /// <summary>Set only when the diagram is interactive; null elsewhere.</summary>
    public Action<GridPos>? Activated { get; set; }

    public bool IsInteractive => Activated is not null;

    [RelayCommand]
    private void Activate() => Activated?.Invoke(Position);

    public string? UpperKey { get; init; }
    public string? UpperSize { get; init; }
    public string? LowerKey { get; init; }
    public string? LowerSize { get; init; }

    public bool HasUpper => UpperKey is not null;
    public bool HasLower => LowerKey is not null;
}

/// <summary>Anything the diagram places at a real position on the virtual desktop.</summary>
public abstract partial class DiagramNodeViewModel : ObservableObject
{
    public required Rect Bounds { get; init; }

    /// <summary>Which gutter this sits in, or the desk itself.</summary>
    public virtual DiagramLane Lane => DiagramLane.Desk;

    /// <summary>Virtual X at which this child's vertical gutter opens.</summary>
    public virtual double LaneAnchor => 0;

    /// <summary>Order among gutters sharing a position; 0 sits nearest the desk.</summary>
    public int LaneSlot { get; set; }

    /// <summary>Thickness of this child's slot, in device pixels.</summary>
    public virtual double LaneThickness => 0;
}

/// <summary>
/// A zone that is not drawable inside a single monitor rectangle: the whole of a
/// display that is already split, or a region crossing a bezel.
/// <para>
/// Drawn as a dimension line alongside the monitors it covers rather than as a
/// rectangle on the desk - a rectangle would sit on top of the zones it contains
/// and hide them, which is exactly what happened while unions had no
/// representation at all and simply vanished from the picture. It is deliberately
/// not panel-shaped: it measures the monitors, it is not one of them.
/// </para>
/// </summary>
public abstract partial class SpanMeasureViewModel : DiagramNodeViewModel
{
    [ObservableProperty]
    private bool _isCapturing;

    public required string Name { get; init; }
    public required string KeyLabel { get; init; }
    public required string SizeLabel { get; init; }

    public GridPos Position { get; init; }
    public Action<GridPos>? Activated { get; set; }
    public bool IsInteractive => Activated is not null;

    [RelayCommand]
    private void Activate() => Activated?.Invoke(Position);
}

/// <summary>A span across displays sitting side by side; measured underneath them.</summary>
public sealed partial class HorizontalSpanViewModel : SpanMeasureViewModel
{
    public override DiagramLane Lane => DiagramLane.Bottom;

    /// <summary>
    /// The upper and lower halves of the span, drawn as chips above and below
    /// the rule - the same "upper / whole / lower" the key rows mean, stated in
    /// the same vertical order. They are separate zones, but three rules stacked
    /// under the desk would all look alike and say nothing about which is which.
    /// </summary>
    public string? UpperKey { get; init; }

    public string? UpperSize { get; init; }
    public string? LowerKey { get; init; }
    public string? LowerSize { get; init; }

    public bool HasUpper => UpperKey is not null;
    public bool HasLower => LowerKey is not null;

    // The label and the chip riding the rule, plus a row for each tier chip.
    // Asymmetric because the span chip is taller than its own row and is raised
    // within it: it reaches up under the text, so the upper tier needs clearing
    // above, while below it already ends short of its row.
    public override double LaneThickness =>
        40 + (HasUpper ? 30 : 0) + (HasLower ? 18 : 0);
}

/// <summary>
/// A span up a stack of displays; measured beside them, not underneath - drawn
/// below, it would appear to measure their combined width, which is not what the
/// key does. The gutter it opens is against its own displays, on whichever of
/// their sides faces the nearer edge of the desk.
/// </summary>
public sealed partial class VerticalSpanViewModel : SpanMeasureViewModel
{
    public override DiagramLane Lane => DiagramLane.VerticalGutter;

    /// <summary>
    /// The gutter opens against this measure's own displays - at their left edge
    /// or their right - so it always sits beside what it describes. Sent to the
    /// far edge of the desk instead, a measure for a middle column appears to
    /// belong to whichever monitor it ends up next to.
    /// </summary>
    public override double LaneAnchor => OnLeftOfDisplays ? Bounds.Left : Bounds.Right;

    /// <summary>True when the measure sits to the left of the displays it covers.</summary>
    public required bool OnLeftOfDisplays { get; init; }

    /// <summary>
    /// The rule sits between the displays and the label, so a measure on their
    /// left has the label outermost and one on their right has the rule.
    /// </summary>
    public int RuleColumn => OnLeftOfDisplays ? 1 : 0;

    public int LabelColumn => OnLeftOfDisplays ? 0 : 1;

    /// <summary>
    /// A measure on the left reads bottom-to-top, one on the right reads
    /// top-to-bottom - so in both cases the text turns away from the displays
    /// and the pair reads outward rather than both leaning the same way.
    /// </summary>
    public bool ReadsUpward => OnLeftOfDisplays;

    public bool ReadsDownward => !ReadsUpward;

    // The turned label, the rule, and the chip - which is wider than the rule
    // it rides and overhangs it on both sides. Trim this and the chip is clipped
    // where the gutter meets the desk.
    public override double LaneThickness => 50;
}

public sealed partial class DisplayNodeViewModel : DiagramNodeViewModel
{
    public required string Key { get; init; }
    public required string Title { get; init; }

    /// <summary>
    /// Resolution, scaling and primary flag. Shown as a tooltip rather than a
    /// second label line: inside a narrow column it collided with the tier key
    /// above it, and stacking it would only make the label taller and the
    /// collision worse. The window header already states the same thing.
    /// </summary>
    public required string Detail { get; init; }
    public required bool IsPrimary { get; init; }

    /// <summary>Taskbar strip as a fraction of the display, so the diagram matches reality.</summary>
    public required Rect TaskbarArea { get; init; }

    public required bool HasTaskbar { get; init; }
    public required IReadOnlyList<ZoneCellViewModel> Cells { get; init; }

    /// <summary>
    /// Whether the name tab has room to sit above this display, outside it.
    /// <para>
    /// Outside is where it belongs: inside, it lands on the zone occupying the
    /// top of the display, and on a monitor split into several stacked zones
    /// those cells are short enough that it covers their key chips. It goes
    /// inside only when another display sits directly above - in a stacked pair
    /// there is no room, and a tab drawn there would appear to label the display
    /// above it.
    /// </para>
    /// </summary>
    public required bool LabelAbove { get; init; }

    /// <summary>Everything the tab shows, for the hover text when it is trimmed.</summary>
    public string TitleAndDetail => $"{Title}  ·  {Detail}";

    public bool LabelInside => !LabelAbove;
}

public sealed partial class MonitorDiagramViewModel : ObservableObject
{
    [ObservableProperty]
    private Rect _virtualBounds;

    [ObservableProperty]
    private IReadOnlyList<DisplayNodeViewModel> _displays = [];

    [ObservableProperty]
    private IReadOnlyList<SpanMeasureViewModel> _spans = [];

    /// <summary>Everything the panel places, monitors and span measures alike.</summary>
    public IEnumerable<DiagramNodeViewModel> Nodes => Displays.Cast<DiagramNodeViewModel>().Concat(Spans);

    /// <param name="onZoneActivated">
    /// When supplied the diagram becomes clickable and each zone reports its
    /// grid position - which is how settings turns the picture into the
    /// rebinding control rather than duplicating it as a list.
    /// </param>
    public static MonitorDiagramViewModel Build(
        IReadOnlyList<DisplayInfo> displays,
        LayoutResult? layout = null,
        Action<GridPos>? onZoneActivated = null)
    {
        var vm = new MonitorDiagramViewModel();
        if (displays.Count == 0) return vm;

        var desk = PxRect.Union(displays.Select(d => d.Bounds));

        // The desk alone: the gutters holding the span measures are reserved by
        // the panel in device pixels, since only it knows the scale.
        vm.VirtualBounds = new Rect(desk.X, desk.Y, desk.Width, desk.Height);

        vm.Displays = [.. displays.Select(d => BuildNode(d, layout, HasClearanceAbove(d, displays)))];
        vm.Spans = BuildSpans(displays, layout, desk);

        if (onZoneActivated is not null)
        {
            foreach (var cell in vm.Displays.SelectMany(d => d.Cells))
                cell.Activated = onZoneActivated;

            foreach (var span in vm.Spans)
                span.Activated = onZoneActivated;
        }

        return vm;
    }

    /// <summary>Mark one cell as awaiting a keypress, clearing any other.</summary>
    public void SetCapturing(GridPos? position)
    {
        foreach (var cell in Displays.SelectMany(d => d.Cells))
            cell.IsCapturing = position is not null && cell.Position == position.Value;

        foreach (var span in Spans)
            span.IsCapturing = position is not null && span.Position == position.Value;
    }

    /// <summary>
    /// Lay each union out as a measure alongside what it covers: a span across
    /// monitors reads underneath them, a span up a stack reads beside them - on
    /// whichever side is nearer the monitors in question, so the measure sits
    /// next to what it describes rather than in one distant column of them.
    /// </summary>
    private static IReadOnlyList<SpanMeasureViewModel> BuildSpans(
        IReadOnlyList<DisplayInfo> displays, LayoutResult? layout, PxRect desk)
    {
        if (layout is null) return [];

        var byKey = displays.ToDictionary(d => d.StableKey);
        var byKeyLookup = displays.ToLookup(d => d.StableKey);
        var deskMiddle = desk.X + desk.Width / 2.0;

        var spans = new List<SpanMeasureViewModel>();
        var home = layout.Surface.HomeRow;

        // A span column binds three keys - upper half, whole, lower half - and
        // they become ONE measure with tier chips, not three rules stacked under
        // the desk saying nothing about which is which.
        var tiers = layout.Zones
            .Where(z => z.Kind == ZoneKind.Union && z.Position.Row != home)
            .ToLookup(z => z.Position.Col);

        foreach (var zone in layout.Zones.Where(z => z.Kind == ZoneKind.Union && z.Position.Row == home))
        {
            var pixels = zone.Parts
                .Where(p => byKey.ContainsKey(p.DisplayKey))
                .Select(p => p.Area.Project(byKey[p.DisplayKey].WorkArea))
                .ToList();

            if (pixels.Count == 0) continue;

            var extent = PxRect.Union(pixels);
            var bounds = new Rect(extent.X, extent.Y, extent.Width, extent.Height);
            var label = layout.Surface.FallbackLabelAt(zone.Position);
            var size = $"{extent.Width} × {extent.Height}";

            if (IsVertical(zone, pixels, byKeyLookup))
            {
                // Beside its own displays either way; this only picks which of
                // their two sides, and the outward one keeps the measure away
                // from the middle of the desk. A centered column has no outward
                // side, and the right is where a reader looks by default.
                var middle = extent.X + extent.Width / 2.0;

                spans.Add(new VerticalSpanViewModel
                {
                    Bounds = bounds,
                    OnLeftOfDisplays = middle < deskMiddle,
                    Name = zone.Name,
                    KeyLabel = label,
                    SizeLabel = size,
                    Position = zone.Position,
                });
            }
            else
            {
                var column = tiers[zone.Position.Col].ToList();
                var upper = column.FirstOrDefault(z => z.Position.Row < home);
                var lower = column.FirstOrDefault(z => z.Position.Row > home);

                spans.Add(new HorizontalSpanViewModel
                {
                    Bounds = bounds,
                    Name = zone.Name,
                    KeyLabel = label,
                    SizeLabel = size,
                    Position = zone.Position,
                    UpperKey = upper is null ? null : layout.Surface.FallbackLabelAt(upper.Position),
                    UpperSize = Extent(upper, byKey),
                    LowerKey = lower is null ? null : layout.Surface.FallbackLabelAt(lower.Position),
                    LowerSize = Extent(lower, byKey),
                });
            }
        }

        // Slot only breaks ties: gutters are ordered by where they open, and
        // horizontal measures stack under the desk in the order they were built.
        foreach (var lane in spans.GroupBy(s => (s.Lane, s.LaneAnchor)))
        {
            var slot = 0;
            foreach (var span in lane.OrderBy(s => s.Bounds.Top)) span.LaneSlot = slot++;
        }

        return spans;
    }

    /// <summary>Pixel size of a zone across every display it touches.</summary>
    private static string? Extent(Zone? zone, IReadOnlyDictionary<string, DisplayInfo> byKey)
    {
        if (zone is null) return null;

        var pixels = zone.Parts
            .Where(p => byKey.ContainsKey(p.DisplayKey))
            .Select(p => p.Area.Project(byKey[p.DisplayKey].WorkArea))
            .ToList();

        if (pixels.Count == 0) return null;

        var extent = PxRect.Union(pixels);
        return $"{extent.Width} × {extent.Height}";
    }

    /// <summary>
    /// Which way the union runs. Taken from how its pieces are laid out, not
    /// from the shape of the bounding box: two stacked ultrawides are wider than
    /// they are tall, yet the span up them is unmistakably vertical.
    /// </summary>
    private static bool IsVertical(
        Zone zone, IReadOnlyList<PxRect> pixels, ILookup<string, DisplayInfo> byKey)
    {
        if (pixels.Count > 1)
        {
            var spreadX = pixels.Max(p => p.X + p.Width / 2.0) - pixels.Min(p => p.X + p.Width / 2.0);
            var spreadY = pixels.Max(p => p.Y + p.Height / 2.0) - pixels.Min(p => p.Y + p.Height / 2.0);
            return spreadY > spreadX;
        }

        // One part means "the whole of this display", which stands in for the
        // pieces it was split into - so it runs along the display's split axis.
        var display = byKey[zone.Parts[0].DisplayKey].FirstOrDefault();
        return display is not null && display.SplitAxis == Axis.Vertical;
    }

    /// <summary>
    /// True when nothing sits immediately above this display, so its name tab
    /// can be drawn outside it. The band checked is proportional to the display
    /// rather than a fixed pixel count, since the diagram is scaled to fit.
    /// </summary>
    private static bool HasClearanceAbove(DisplayInfo display, IReadOnlyList<DisplayInfo> all)
    {
        var band = Math.Max(40, display.Bounds.Height / 8);

        foreach (var other in all)
        {
            if (ReferenceEquals(other, display)) continue;
            if (other.Bounds.HorizontalOverlap(display.Bounds) <= 0) continue;

            var gap = display.Bounds.Top - other.Bounds.Bottom;
            if (gap >= 0 && gap < band) return false;
        }

        return true;
    }

    private static DisplayNodeViewModel BuildNode(DisplayInfo display, LayoutResult? layout, bool labelAbove)
    {
        var taskbarHeight = display.Bounds.Height - display.WorkArea.Height;

        return new DisplayNodeViewModel
        {
            Key = display.StableKey,
            Title = display.FriendlyName,
            Detail = $"{display.Bounds.Width} × {display.Bounds.Height}" +
                     (display.Dpi != 96 ? $"  ·  {display.Scale:P0}" : string.Empty) +
                     (display.IsPrimary ? "  ·  primary" : string.Empty),
            Bounds = new Rect(display.Bounds.X, display.Bounds.Y, display.Bounds.Width, display.Bounds.Height),
            IsPrimary = display.IsPrimary,
            HasTaskbar = taskbarHeight > 0,
            TaskbarArea = taskbarHeight > 0
                ? new Rect(0, 1.0 - (double)taskbarHeight / display.Bounds.Height, 1,
                           (double)taskbarHeight / display.Bounds.Height)
                : default,
            Cells = BuildCells(display, layout),
            LabelAbove = labelAbove,
        };
    }

    private static IReadOnlyList<ZoneCellViewModel> BuildCells(DisplayInfo display, LayoutResult? layout)
    {
        if (layout is null) return [];

        // Unions are drawn as bars below, not as cells. Leaving them in would
        // wreck the column clustering: a zone spanning the whole display
        // overlaps every column, so the union-find pulls all of them into one
        // group and only the first survives - which is how the halves of a
        // split monitor disappeared from the picture entirely.
        var onThisDisplay = layout.Zones
            .Where(z => z.Kind != ZoneKind.Union)
            .Where(z => z.Parts.Any(p => p.DisplayKey == display.StableKey))
            .ToList();

        var cells = new List<ZoneCellViewModel>();

        // Group by GEOMETRY, not by grid column. Rebinding lets a zone's key
        // move without its rectangle moving, so grid position and physical
        // position diverge - grouping by column then draws unrelated zones on
        // top of each other.
        foreach (var column in GroupByHorizontalSpan(onThisDisplay, display))
        {
            var entries = column
                .Select(z =>
                {
                    var part = z.Parts.First(p => p.DisplayKey == display.StableKey);
                    return (Zone: z, Part: part, Pixels: part.Area.Project(display.WorkArea));
                })
                .OrderBy(e => e.Part.Area.Y)
                .ToList();

            // The tallest zone is the one the others sit inside.
            var primary = entries.MaxBy(e => e.Part.Area.H);

            var overlapping = entries.Count > 1 && entries
                .Where(e => e.Zone != primary.Zone)
                .All(e => e.Part.Area.Y >= primary.Part.Area.Y - 1e-6 &&
                          e.Part.Area.Bottom <= primary.Part.Area.Bottom + 1e-6);

            if (!overlapping)
            {
                // Zones genuinely tile this column (e.g. a portrait display split
                // into stacked thirds); draw each as its own rectangle.
                foreach (var e in entries)
                {
                    cells.Add(new ZoneCellViewModel
                    {
                        Name = e.Zone.Name,
                        KeyLabel = layout.Surface.FallbackLabelAt(e.Zone.Position),
                        Area = ToDisplayFraction(e.Part.Area, display),
                        SizeLabel = $"{e.Pixels.Width} × {e.Pixels.Height}",
                        SpansDisplays = e.Zone.SpansDisplays,
                        Position = e.Zone.Position,
                    });
                }

                continue;
            }

            var mid = primary.Part.Area.Y + primary.Part.Area.H / 2;
            var upper = entries.FirstOrDefault(e => e.Zone != primary.Zone && e.Part.Area.Bottom <= mid + 1e-6);
            var lower = entries.FirstOrDefault(e => e.Zone != primary.Zone && e.Part.Area.Y >= mid - 1e-6);

            cells.Add(new ZoneCellViewModel
            {
                Name = primary.Zone.Name,
                KeyLabel = layout.Surface.FallbackLabelAt(primary.Zone.Position),
                Area = ToDisplayFraction(primary.Part.Area, display),
                SizeLabel = $"{primary.Pixels.Width} × {primary.Pixels.Height}",
                SpansDisplays = primary.Zone.SpansDisplays,
                Position = primary.Zone.Position,
                UpperKey = upper.Zone is null ? null : layout.Surface.FallbackLabelAt(upper.Zone.Position),
                UpperSize = upper.Zone is null ? null : $"{upper.Pixels.Width} × {upper.Pixels.Height}",
                LowerKey = lower.Zone is null ? null : layout.Surface.FallbackLabelAt(lower.Zone.Position),
                LowerSize = lower.Zone is null ? null : $"{lower.Pixels.Width} × {lower.Pixels.Height}",
            });
        }

        return cells;
    }

    /// <summary>
    /// Cluster zones into visual columns by how much their horizontal spans
    /// overlap, ordered left to right.
    /// </summary>
    private static IEnumerable<IReadOnlyList<Zone>> GroupByHorizontalSpan(
        IReadOnlyList<Zone> zones, DisplayInfo display)
    {
        var remaining = zones
            .Select(z => (Zone: z, Area: z.Parts.First(p => p.DisplayKey == display.StableKey).Area))
            .OrderBy(x => x.Area.X)
            .ToList();

        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            remaining.RemoveAt(0);

            var group = new List<Zone> { seed.Zone };
            var left = seed.Area.X;
            var right = seed.Area.Right;

            // Repeat until nothing new joins: a wide zone can pull in others
            // that did not overlap the original seed.
            bool added;
            do
            {
                added = false;

                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    var candidate = remaining[i];
                    var overlap = Math.Min(right, candidate.Area.Right) - Math.Max(left, candidate.Area.X);
                    var narrower = Math.Min(right - left, candidate.Area.W);

                    if (narrower > 0 && overlap > narrower * 0.5)
                    {
                        group.Add(candidate.Zone);
                        left = Math.Min(left, candidate.Area.X);
                        right = Math.Max(right, candidate.Area.Right);
                        remaining.RemoveAt(i);
                        added = true;
                    }
                }
            }
            while (added);

            yield return group;
        }
    }

    /// <summary>
    /// Rebase a work-area fraction onto the full display, since the diagram draws
    /// the taskbar strip too - otherwise zones would appear to cover the taskbar.
    /// </summary>
    private static Rect ToDisplayFraction(NormRect area, DisplayInfo display)
    {
        var work = display.WorkArea;
        var full = display.Bounds;

        return new Rect(
            (work.X - full.X + area.X * work.Width) / full.Width,
            (work.Y - full.Y + area.Y * work.Height) / full.Height,
            area.W * work.Width / full.Width,
            area.H * work.Height / full.Height);
    }
}
