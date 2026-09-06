using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public sealed partial class DisplayNodeViewModel : ObservableObject
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
    public required Rect Bounds { get; init; }
    public required bool IsPrimary { get; init; }

    /// <summary>Taskbar strip as a fraction of the display, so the diagram matches reality.</summary>
    public required Rect TaskbarArea { get; init; }

    public required bool HasTaskbar { get; init; }
    public required IReadOnlyList<ZoneCellViewModel> Cells { get; init; }
}

public sealed partial class MonitorDiagramViewModel : ObservableObject
{
    [ObservableProperty]
    private Rect _virtualBounds;

    [ObservableProperty]
    private IReadOnlyList<DisplayNodeViewModel> _displays = [];

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

        var union = PxRect.Union(displays.Select(d => d.Bounds));
        vm.VirtualBounds = new Rect(union.X, union.Y, union.Width, union.Height);

        vm.Displays = [.. displays.Select(d => BuildNode(d, layout))];

        if (onZoneActivated is not null)
        {
            foreach (var cell in vm.Displays.SelectMany(d => d.Cells))
                cell.Activated = onZoneActivated;
        }

        return vm;
    }

    /// <summary>Mark one cell as awaiting a keypress, clearing any other.</summary>
    public void SetCapturing(GridPos? position)
    {
        foreach (var cell in Displays.SelectMany(d => d.Cells))
            cell.IsCapturing = position is not null && cell.Position == position.Value;
    }

    private static DisplayNodeViewModel BuildNode(DisplayInfo display, LayoutResult? layout)
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
        };
    }

    private static IReadOnlyList<ZoneCellViewModel> BuildCells(DisplayInfo display, LayoutResult? layout)
    {
        if (layout is null) return [];

        var onThisDisplay = layout.Zones
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
