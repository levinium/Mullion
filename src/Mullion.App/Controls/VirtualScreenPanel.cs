using Avalonia;
using Avalonia.Controls;

namespace Mullion.App.Controls;

/// <summary>Where a child sits: on the desk itself, or in a gutter beside it.</summary>
public enum DiagramLane
{
    Desk,

    /// <summary>
    /// A vertical gutter opened at a given point across the desk, pushing
    /// everything beyond it aside. Its anchor is a virtual X, so the gutter
    /// lands against the very displays its measure describes instead of at the
    /// far edge of the desk - a measure parked beyond an unrelated monitor
    /// reads as belonging to that monitor.
    /// </summary>
    VerticalGutter,

    /// <summary>A horizontal gutter below the desk.</summary>
    Bottom,
}

/// <summary>
/// Lays children out at their true relative positions and sizes within the
/// virtual desktop, scaled uniformly to fit and centered.
/// <para>
/// A Panel with attached properties rather than a custom Render override, so
/// children stay real controls: hit-testing, hover, tooltips, focus, keyboard
/// navigation and transitions all come for free, which matters because the
/// zone tiles need to be clickable.
/// </para>
/// <para>
/// Annotations - the span measures - go in gutters whose thickness is stated in
/// DEVICE pixels and reserved before the scale is chosen. Expressing a gutter in
/// virtual pixels instead cannot work: the scale is set by the desk, so on a
/// tall arrangement the same gutter collapses to a few pixels and crops the
/// label it exists to hold. Displays keep their true sizes and their true order;
/// only the space between two of them grows, which is what a dimension drawing
/// does anyway.
/// </para>
/// </summary>
public sealed class VirtualScreenPanel : Panel
{
    private const double LaneGap = 4;

    public static readonly StyledProperty<Rect> VirtualBoundsProperty =
        AvaloniaProperty.Register<VirtualScreenPanel, Rect>(nameof(VirtualBounds));

    public static readonly AttachedProperty<Rect> RectProperty =
        AvaloniaProperty.RegisterAttached<VirtualScreenPanel, Control, Rect>("Rect");

    public static readonly AttachedProperty<DiagramLane> LaneProperty =
        AvaloniaProperty.RegisterAttached<VirtualScreenPanel, Control, DiagramLane>("Lane");

    /// <summary>Virtual X at which a vertical gutter opens.</summary>
    public static readonly AttachedProperty<double> LaneAnchorProperty =
        AvaloniaProperty.RegisterAttached<VirtualScreenPanel, Control, double>("LaneAnchor");

    /// <summary>Order among gutters sharing an anchor; 0 sits nearest the desk.</summary>
    public static readonly AttachedProperty<int> LaneSlotProperty =
        AvaloniaProperty.RegisterAttached<VirtualScreenPanel, Control, int>("LaneSlot");

    /// <summary>
    /// The LEAST thick this child's gutter may be, in device pixels. The actual
    /// thickness is whatever the child measures, so a gutter grows to fit its
    /// content instead of cropping it - which is what a fixed number did the
    /// moment key chips went from "A" to "Win+A".
    /// </summary>
    public static readonly AttachedProperty<double> LaneThicknessProperty =
        AvaloniaProperty.RegisterAttached<VirtualScreenPanel, Control, double>("LaneThickness", 32);

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<VirtualScreenPanel, double>(nameof(Gap), 4.0);

    /// <summary>
    /// Device pixels held clear above the desk, for the name tabs that sit on
    /// top of each monitor.
    /// <para>
    /// Reserved space rather than pure overflow: a tab pushed above the desk by
    /// a negative margin alone lands outside the panel, which is sized exactly
    /// to its content, and an ancestor crops it. Held here it stays inside the
    /// panel and simply draws where nothing else does.
    /// </para>
    /// </summary>
    public static readonly StyledProperty<double> TopHeadroomProperty =
        AvaloniaProperty.Register<VirtualScreenPanel, double>(nameof(TopHeadroom));

    static VirtualScreenPanel()
    {
        AffectsMeasure<VirtualScreenPanel>(VirtualBoundsProperty, GapProperty, TopHeadroomProperty);
        AffectsArrange<VirtualScreenPanel>(VirtualBoundsProperty, GapProperty, TopHeadroomProperty);

        foreach (var p in new AvaloniaProperty[]
                 {
                     RectProperty, LaneProperty, LaneAnchorProperty,
                     LaneSlotProperty, LaneThicknessProperty,
                 })
        {
            p.Changed.AddClassHandler<Control>((c, _) =>
            {
                if (c.Parent is not VirtualScreenPanel panel) return;
                panel.InvalidateMeasure();
                panel.InvalidateArrange();
            });
        }
    }

    public Rect VirtualBounds
    {
        get => GetValue(VirtualBoundsProperty);
        set => SetValue(VirtualBoundsProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double TopHeadroom
    {
        get => GetValue(TopHeadroomProperty);
        set => SetValue(TopHeadroomProperty, value);
    }

    /// <summary>Scale actually used at the last arrange, for drag-to-resize maths.</summary>
    public double CurrentScale { get; private set; } = 1;

    public static void SetRect(Control element, Rect value) => element.SetValue(RectProperty, value);

    public static Rect GetRect(Control element) => element.GetValue(RectProperty);

    public static void SetLane(Control element, DiagramLane value) => element.SetValue(LaneProperty, value);

    public static DiagramLane GetLane(Control element) => element.GetValue(LaneProperty);

    public static void SetLaneAnchor(Control element, double value) => element.SetValue(LaneAnchorProperty, value);

    public static double GetLaneAnchor(Control element) => element.GetValue(LaneAnchorProperty);

    public static void SetLaneSlot(Control element, int value) => element.SetValue(LaneSlotProperty, value);

    public static int GetLaneSlot(Control element) => element.GetValue(LaneSlotProperty);

    public static void SetLaneThickness(Control element, double value) =>
        element.SetValue(LaneThicknessProperty, value);

    public static double GetLaneThickness(Control element) => element.GetValue(LaneThicknessProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);

        var bounds = VirtualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return default;

        var gutters = Gutters();
        var inserted = gutters.Sum(g => g.Thickness);
        var below = BottomExtent();

        // Infinity in either direction means "size to content"; fall back to the
        // other axis so the panel still reports something sensible inside a
        // ScrollViewer or an auto-sized parent.
        var width = double.IsInfinity(availableSize.Width) ? bounds.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? bounds.Height : availableSize.Height;

        var headroom = TopHeadroom;
        var scale = Scale(width - inserted, height - below - headroom, bounds);

        return new Size(bounds.Width * scale + inserted, bounds.Height * scale + below + headroom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = VirtualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return finalSize;

        var gutters = Gutters();
        var inserted = gutters.Sum(g => g.Thickness);
        var below = BottomExtent();

        var headroom = TopHeadroom;
        var deskWidth = finalSize.Width - inserted;
        var deskHeight = finalSize.Height - below - headroom;
        var scale = Scale(deskWidth, deskHeight, bounds);

        CurrentScale = scale;

        var offsetX = (deskWidth - bounds.Width * scale) / 2;
        var offsetY = headroom + (deskHeight - bounds.Height * scale) / 2;
        var deskBottom = offsetY + bounds.Height * scale;
        var halfGap = Gap / 2;

        // A gutter opens BETWEEN two displays, so an edge that starts at the
        // anchor is pushed past it while an edge that ends there is not. Using
        // the same test for both would stretch the display to its left.
        double ShiftAtStart(double x) => gutters.Where(g => g.Anchor <= x).Sum(g => g.Thickness);
        double ShiftAtEnd(double x) => gutters.Where(g => g.Anchor < x).Sum(g => g.Thickness);

        foreach (var child in Children)
        {
            var rect = GetRect(child);
            var lane = GetLane(child);

            if (lane == DiagramLane.VerticalGutter)
            {
                var index = gutters.FindIndex(g => ReferenceEquals(g.Child, child));
                if (index < 0) continue;

                var before = gutters.Take(index).Sum(g => g.Thickness);
                var x = offsetX + (gutters[index].Anchor - bounds.X) * scale + before;

                child.Arrange(new Rect(
                    x + LaneGap / 2,
                    offsetY + (rect.Y - bounds.Y) * scale,
                    Math.Max(0, gutters[index].Thickness - LaneGap),
                    rect.Height * scale));

                continue;
            }

            var left = offsetX + (rect.X - bounds.X) * scale + ShiftAtStart(rect.X);
            var right = offsetX + (rect.Right - bounds.X) * scale + ShiftAtEnd(rect.Right);
            var w = Math.Max(0, right - left);

            if (lane == DiagramLane.Bottom)
            {
                var slotOffset = BottomOffset(GetLaneSlot(child));
                child.Arrange(new Rect(left, deskBottom + slotOffset, w, BottomThickness(child)));
                continue;
            }

            var top = offsetY + (rect.Y - bounds.Y) * scale;
            var h = rect.Height * scale;

            // Inset by the gap so adjacent monitors read as separate panels
            // rather than one continuous slab, without distorting position.
            var inset = Math.Min(halfGap, Math.Min(w, h) / 4);

            child.Arrange(new Rect(left + inset, top + inset,
                Math.Max(0, w - inset * 2), Math.Max(0, h - inset * 2)));
        }

        return finalSize;
    }

    private static double Scale(double width, double height, Rect bounds)
    {
        var scale = Math.Min(width / bounds.Width, height / bounds.Height);
        return double.IsInfinity(scale) || scale <= 0 ? 1 : scale;
    }

    /// <summary>Vertical gutters in the order they open across the desk.</summary>
    private List<(Control Child, double Anchor, double Thickness)> Gutters() =>
        [.. Children
            .Where(c => GetLane(c) == DiagramLane.VerticalGutter)
            .Select(c => (
                Child: c,
                Anchor: GetLaneAnchor(c),
                Thickness: Math.Max(GetLaneThickness(c), c.DesiredSize.Width) + LaneGap))
            .OrderBy(g => g.Anchor)
            .ThenBy(g => GetLaneSlot(g.Child))];

    private double BottomExtent()
    {
        double total = 0;

        foreach (var child in Children)
        {
            if (GetLane(child) != DiagramLane.Bottom) continue;
            total += BottomThickness(child) + LaneGap;
        }

        return total;
    }

    private double BottomOffset(int slot)
    {
        double total = LaneGap;

        foreach (var child in Children)
        {
            if (GetLane(child) != DiagramLane.Bottom) continue;
            if (GetLaneSlot(child) >= slot) continue;
            total += BottomThickness(child) + LaneGap;
        }

        return total;
    }

    private static double BottomThickness(Control child) =>
        Math.Max(GetLaneThickness(child), child.DesiredSize.Height);
}
