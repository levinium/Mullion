using Avalonia;
using Avalonia.Controls;

namespace Mullion.App.Controls;

/// <summary>
/// Arranges children by a 0..1 fractional rect within its own bounds.
/// <para>
/// The inner half of the diagram: nested inside a <see cref="VirtualScreenPanel"/>
/// it gives zone tiles correct scaling with no manual arithmetic in XAML, and it
/// takes the same NormRect fractions the layout engine already produces.
/// </para>
/// </summary>
public sealed class NormalizedPanel : Panel
{
    public static readonly AttachedProperty<Rect> AreaProperty =
        AvaloniaProperty.RegisterAttached<NormalizedPanel, Control, Rect>("Area", new Rect(0, 0, 1, 1));

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<NormalizedPanel, double>(nameof(Gap), 2.0);

    static NormalizedPanel()
    {
        AffectsArrange<NormalizedPanel>(GapProperty);
        AreaProperty.Changed.AddClassHandler<Control>((c, _) =>
            (c.Parent as NormalizedPanel)?.InvalidateArrange());
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public static void SetArea(Control element, Rect value) => element.SetValue(AreaProperty, value);

    public static Rect GetArea(Control element) => element.GetValue(AreaProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return default;   // fills whatever the parent gives it
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var halfGap = Gap / 2;

        foreach (var child in Children)
        {
            var area = GetArea(child);

            var x = area.X * finalSize.Width;
            var y = area.Y * finalSize.Height;
            var w = area.Width * finalSize.Width;
            var h = area.Height * finalSize.Height;

            var inset = Math.Min(halfGap, Math.Min(w, h) / 4);

            child.Arrange(new Rect(x + inset, y + inset,
                Math.Max(0, w - inset * 2), Math.Max(0, h - inset * 2)));
        }

        return finalSize;
    }
}
