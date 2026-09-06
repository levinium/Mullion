using Avalonia;
using Avalonia.Controls;

namespace Mullion.App.Controls;

/// <summary>
/// Lays children out at their true relative positions and sizes within the
/// virtual desktop, scaled uniformly to fit and centered.
/// <para>
/// A Panel with attached properties rather than a custom Render override, so
/// children stay real controls: hit-testing, hover, tooltips, focus, keyboard
/// navigation and transitions all come for free, which matters because the
/// zone tiles need to be clickable.
/// </para>
/// </summary>
public sealed class VirtualScreenPanel : Panel
{
    public static readonly StyledProperty<Rect> VirtualBoundsProperty =
        AvaloniaProperty.Register<VirtualScreenPanel, Rect>(nameof(VirtualBounds));

    public static readonly AttachedProperty<Rect> RectProperty =
        AvaloniaProperty.RegisterAttached<VirtualScreenPanel, Control, Rect>("Rect");

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<VirtualScreenPanel, double>(nameof(Gap), 4.0);

    static VirtualScreenPanel()
    {
        AffectsMeasure<VirtualScreenPanel>(VirtualBoundsProperty, GapProperty);
        AffectsArrange<VirtualScreenPanel>(VirtualBoundsProperty, GapProperty);
        RectProperty.Changed.AddClassHandler<Control>((c, _) =>
            (c.Parent as VirtualScreenPanel)?.InvalidateArrange());
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

    /// <summary>Scale actually used at the last arrange, for drag-to-resize maths.</summary>
    public double CurrentScale { get; private set; } = 1;

    public static void SetRect(Control element, Rect value) => element.SetValue(RectProperty, value);

    public static Rect GetRect(Control element) => element.GetValue(RectProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);

        var bounds = VirtualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return default;

        // Infinity in either direction means "size to content"; fall back to the
        // other axis so the panel still reports something sensible inside a
        // ScrollViewer or an auto-sized parent.
        var width = double.IsInfinity(availableSize.Width) ? bounds.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? bounds.Height : availableSize.Height;

        var scale = Math.Min(width / bounds.Width, height / bounds.Height);
        if (double.IsInfinity(scale) || scale <= 0) scale = 1;

        return new Size(bounds.Width * scale, bounds.Height * scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = VirtualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return finalSize;

        var scale = Math.Min(finalSize.Width / bounds.Width, finalSize.Height / bounds.Height);
        if (double.IsInfinity(scale) || scale <= 0) scale = 1;

        CurrentScale = scale;

        var offsetX = (finalSize.Width - bounds.Width * scale) / 2;
        var offsetY = (finalSize.Height - bounds.Height * scale) / 2;
        var halfGap = Gap / 2;

        foreach (var child in Children)
        {
            var rect = GetRect(child);

            var x = offsetX + (rect.X - bounds.X) * scale;
            var y = offsetY + (rect.Y - bounds.Y) * scale;
            var w = rect.Width * scale;
            var h = rect.Height * scale;

            // Inset by the gap so adjacent monitors read as separate panels
            // rather than one continuous slab, without distorting position.
            var inset = Math.Min(halfGap, Math.Min(w, h) / 4);

            child.Arrange(new Rect(x + inset, y + inset,
                Math.Max(0, w - inset * 2), Math.Max(0, h - inset * 2)));
        }

        return finalSize;
    }
}
