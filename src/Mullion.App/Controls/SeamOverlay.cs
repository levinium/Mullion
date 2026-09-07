using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Mullion.App.Controls;

/// <summary>
/// Places children on the seams between a display's zones.
/// <para>
/// A seam has a position but no width - it is the line where two zones meet -
/// while the thing you grab has to be wide enough to hit. NormalizedPanel cannot
/// express that: it arranges a child at exactly the fraction it is given, so a
/// seam laid out there would be zero pixels wide and unclickable. Here the
/// position is normalized and the thickness is in pixels, which is the only
/// combination that gives a grabbable target on a monitor drawn at any scale.
/// </para>
/// </summary>
public sealed class SeamOverlay : Panel
{
    /// <summary>Where along the split axis this seam sits, 0..1 of the display.</summary>
    public static readonly AttachedProperty<double> PositionProperty =
        AvaloniaProperty.RegisterAttached<SeamOverlay, Control, double>("Position");

    /// <summary>
    /// The seam's extent across the other axis, 0..1. Zones stop at the taskbar,
    /// so a seam drawn the full height of the display would stick out past them.
    /// </summary>
    public static readonly AttachedProperty<double> FromProperty =
        AvaloniaProperty.RegisterAttached<SeamOverlay, Control, double>("From");

    public static readonly AttachedProperty<double> ToProperty =
        AvaloniaProperty.RegisterAttached<SeamOverlay, Control, double>("To", 1.0);

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<SeamOverlay, Orientation>(nameof(Orientation));

    /// <summary>How wide the grab target is, in device pixels.</summary>
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<SeamOverlay, double>(nameof(Thickness), 12.0);

    static SeamOverlay()
    {
        AffectsArrange<SeamOverlay>(OrientationProperty, ThicknessProperty);

        foreach (var property in new AvaloniaProperty[] { PositionProperty, FromProperty, ToProperty })
            property.Changed.AddClassHandler<Control>((c, _) =>
                (c.GetVisualParent() as SeamOverlay)?.InvalidateArrange());
    }

    public static void SetPosition(Control element, double value) => element.SetValue(PositionProperty, value);
    public static double GetPosition(Control element) => element.GetValue(PositionProperty);
    public static void SetFrom(Control element, double value) => element.SetValue(FromProperty, value);
    public static double GetFrom(Control element) => element.GetValue(FromProperty);
    public static void SetTo(Control element, double value) => element.SetValue(ToProperty, value);
    public static double GetTo(Control element) => element.GetValue(ToProperty);

    /// <summary>The axis the seams run ACROSS - Horizontal for a left-to-right split.</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(availableSize);

        // Never asks for room of its own: it is an overlay on the zones, and a
        // desired size here would push the display's own layout around.
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var half = Thickness / 2;

        foreach (var child in Children)
        {
            var at = GetPosition(child);
            var from = GetFrom(child);
            var to = GetTo(child);

            child.Arrange(Orientation == Orientation.Horizontal
                ? new Rect(at * finalSize.Width - half, from * finalSize.Height,
                           Thickness, Math.Max(0, (to - from) * finalSize.Height))
                : new Rect(from * finalSize.Width, at * finalSize.Height - half,
                           Math.Max(0, (to - from) * finalSize.Width), Thickness));
        }

        return finalSize;
    }
}
