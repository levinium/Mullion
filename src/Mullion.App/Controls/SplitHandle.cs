using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Mullion.App.Controls;

/// <summary>
/// The grab target on a seam between two zones. Reports where it has been
/// dragged to as a fraction of the display it sits on.
/// <para>
/// A fraction rather than a pixel delta because that is what the layout is
/// stored in: zones are fractions of a work area so they survive a resolution
/// change, and a drag speaking in pixels would have to be converted back against
/// a diagram scale the view model has no business knowing. Reporting an absolute
/// position rather than a delta also means a drag cannot accumulate error - the
/// seam lands where the cursor is, however many move events arrived.
/// </para>
/// </summary>
public sealed class SplitHandle : TemplatedControl
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<SplitHandle, Orientation>(nameof(Orientation));

    /// <summary>Which seam this is, passed back with every drag.</summary>
    public static readonly StyledProperty<int> IndexProperty =
        AvaloniaProperty.Register<SplitHandle, int>(nameof(Index));

    /// <summary>
    /// Called continuously with the seam index and where it has been dragged to.
    /// <para>
    /// A bound delegate rather than an event because the handle is created by a
    /// template, one per seam, and there is nowhere to attach a handler: the
    /// view model arrives as the DataContext, so a bindable property is the only
    /// thing a template can wire up. A command would serve as well but cannot
    /// carry two arguments without a converter for the pair.
    /// </para>
    /// </summary>
    public static readonly StyledProperty<Action<int, double>?> DraggedProperty =
        AvaloniaProperty.Register<SplitHandle, Action<int, double>?>(nameof(Dragged));

    /// <summary>Called on release. What the layout is actually saved on.</summary>
    public static readonly StyledProperty<Action?> ReleasedProperty =
        AvaloniaProperty.Register<SplitHandle, Action?>(nameof(Released));

    public static readonly DirectProperty<SplitHandle, bool> IsDraggingProperty =
        AvaloniaProperty.RegisterDirect<SplitHandle, bool>(nameof(IsDragging), o => o._dragging);

    private bool _dragging;

    /// <summary>The axis the seam moves ALONG - Horizontal for a left-to-right split.</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public int Index
    {
        get => GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }

    public Action<int, double>? Dragged
    {
        get => GetValue(DraggedProperty);
        set => SetValue(DraggedProperty, value);
    }

    public Action? Released
    {
        get => GetValue(ReleasedProperty);
        set => SetValue(ReleasedProperty, value);
    }

    /// <summary>Drives the pressed styling, so the seam looks held while it is.</summary>
    public bool IsDragging
    {
        get => _dragging;
        private set => SetAndRaise(IsDraggingProperty, ref _dragging, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Pointer.Capture(this);
        IsDragging = true;

        // Otherwise the zone tile underneath takes the click and starts a key
        // rebind, so grabbing a seam would also ask which key to press.
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!IsDragging) return;

        var fraction = FractionAt(e);
        if (fraction is not null) Dragged?.Invoke(Index, fraction.Value);

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!IsDragging) return;

        e.Pointer.Capture(null);
        IsDragging = false;

        // Committed even when the pointer never moved: a click on a seam is a
        // drag of zero distance, and saving it writes back what is already there
        // rather than doing nothing and looking broken.
        Released?.Invoke();
        e.Handled = true;
    }

    /// <summary>
    /// Where the pointer is, as a fraction of the panel the seams are laid out
    /// on - which is the display, since that is what the overlay covers.
    /// <para>
    /// Found by walking up rather than taking the direct parent: an ItemsControl
    /// wraps every item in a ContentPresenter, so a handle built from a template
    /// is a grandchild of the panel. Reading the parent gave the presenter, no
    /// fraction could be computed, and the seam moved under the cursor without
    /// ever reporting where to.
    /// </para>
    /// </summary>
    private double? FractionAt(PointerEventArgs e)
    {
        if (this.FindAncestorOfType<SeamOverlay>() is not { } overlay) return null;

        var point = e.GetPosition(overlay);
        var size = overlay.Bounds.Size;

        return Orientation == Orientation.Horizontal
            ? size.Width > 0 ? point.X / size.Width : null
            : size.Height > 0 ? point.Y / size.Height : null;
    }
}
