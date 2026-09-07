using Avalonia;
using Avalonia.Input;
using Mullion.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mullion.App.Controls;

public partial class MonitorDiagram : UserControl
{
    /// <summary>
    /// Thumbnail mode: keys only, no sizes or monitor detail.
    /// <para>
    /// At preview-card size the full labelling collides with itself - a 270x110
    /// card cannot legibly carry a monitor name, resolution, three zone names
    /// and six pixel sizes. The key chips alone are what the choice actually
    /// turns on, so everything else is dropped rather than shrunk.
    /// </para>
    /// </summary>
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<MonitorDiagram, bool>(nameof(Compact));

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public bool Detailed => !Compact;

    /// <summary>
    /// In detailed mode the tier chips are pushed down to clear the monitor
    /// label; in compact mode there is no label, so they sit at the true top.
    /// </summary>
    public Thickness TierTopMargin => Compact ? new Thickness(0) : new Thickness(0, 22, 0, 0);

    static MonitorDiagram()
    {
        CompactProperty.Changed.AddClassHandler<MonitorDiagram>((d, _) =>
        {
            d.RaisePropertyChanged(DetailedProperty, !d.Detailed, d.Detailed);
            d.RaisePropertyChanged(TierTopMarginProperty, default, d.TierTopMargin);
        });

        // ModifierSegments is derived, so it has to be told when its source
        // changed or every chip goes on rendering the previous modifier.
        ModifierPrefixProperty.Changed.AddClassHandler<MonitorDiagram>(
            (d, _) => d.RaisePropertyChanged(ModifierSegmentsProperty, [], d.ModifierSegments));
    }

    private static readonly DirectProperty<MonitorDiagram, bool> DetailedProperty =
        AvaloniaProperty.RegisterDirect<MonitorDiagram, bool>(nameof(Detailed), o => o.Detailed);

    private static readonly DirectProperty<MonitorDiagram, Thickness> TierTopMarginProperty =
        AvaloniaProperty.RegisterDirect<MonitorDiagram, Thickness>(nameof(TierTopMargin), o => o.TierTopMargin);

    /// <summary>
    /// Shown before each key, so a chip reads as the whole chord rather than a
    /// bare letter nobody can act on without being told the modifier separately.
    /// </summary>
    public static readonly StyledProperty<string> ModifierPrefixProperty =
        AvaloniaProperty.Register<MonitorDiagram, string>(nameof(ModifierPrefix), "Win+");

    /// <summary>Change notification for the derived segment list.</summary>
    public static readonly DirectProperty<MonitorDiagram, IReadOnlyList<string>> ModifierSegmentsProperty =
        AvaloniaProperty.RegisterDirect<MonitorDiagram, IReadOnlyList<string>>(
            nameof(ModifierSegments), d => d.ModifierSegments);

    public string ModifierPrefix
    {
        get => GetValue(ModifierPrefixProperty);
        set => SetValue(ModifierPrefixProperty, value);
    }

    /// <summary>
    /// The prefix split into the pieces a chord may be broken between, e.g.
    /// "Ctrl+" and "Shift+".
    /// <para>
    /// Given as separate runs rather than one string because text layout cannot
    /// be talked into breaking only where we want. A plain "Ctrl+Shift+" breaks
    /// on either side of a plus, leaving a lone "+" on a line reading as a key;
    /// gluing the pluses on with word joiners makes the whole chord one
    /// unbreakable word, and a chip narrower than that gets it split
    /// mid-letter - "Ctrl / +Sh / ift+". As separate runs in a WrapPanel the
    /// only possible breaks are the ones we chose, and a run too wide to fit
    /// overflows for the Viewbox to scale rather than being cut in half.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ModifierSegments =>
        [.. ModifierPrefix
            .Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => $"{part}+")];


    // ---- Hover hints --------------------------------------------------------

    /// <summary>
    /// Report what the pointer is over, so the editor can name it in its own
    /// line instead of a tooltip.
    /// <para>
    /// Tooltips were doing this and doing it badly: a tooltip is a window of its
    /// own, so it takes the pointer from the thing it is describing. The region
    /// underneath lost its highlight the moment the tip appeared, and a click
    /// aimed at the zone landed on the popup.
    /// </para>
    /// </summary>

    /// <summary>
    /// Keep the floating label beside the pointer and inside the diagram.
    /// <para>
    /// Below and right of the cursor where there is room, above and left where
    /// there is not - so it never runs off the edge, and never sits under the
    /// hand that is pointing.
    /// </para>
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (DataContext is not MonitorDiagramViewModel vm || vm.HoverHint is null) return;

        var at = e.GetPosition(this);

        // Room for the longest hint the diagram produces. Measuring the label
        // itself would be exact, but it has not been arranged yet on the frame
        // that first shows it, and a label that jumps on its second frame is
        // worse than one that reserves a little too much.
        const double Width = 250;
        const double Height = 26;
        const double Gap = 18;

        vm.HintX = at.X + Gap + Width < Bounds.Width ? at.X + Gap : Math.Max(0, at.X - Gap - Width);
        vm.HintY = at.Y + Gap + Height < Bounds.Height ? at.Y + Gap : Math.Max(0, at.Y - Gap - Height);
    }

    private void OnWholeEntered(object? sender, PointerEventArgs e) =>
        Hint((sender as Control)?.DataContext, c => c.WholeHint);

    /// <summary>A span measure names itself the same way a zone does.</summary>
    private void OnSpanEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MonitorDiagramViewModel vm) return;
        if ((sender as Control)?.DataContext is not SpanMeasureViewModel span) return;

        vm.HoverHint = span.IsInteractive ? span.WholeHint : null;
    }

    private void OnUpperEntered(object? sender, PointerEventArgs e) =>
        Hint((sender as Control)?.DataContext, c => c.UpperHint);

    private void OnLowerEntered(object? sender, PointerEventArgs e) =>
        Hint((sender as Control)?.DataContext, c => c.LowerHint);

    private void OnHoverLeft(object? sender, PointerEventArgs e)
    {
        if (DataContext is MonitorDiagramViewModel vm) vm.HoverHint = null;
    }

    private void Hint(object? context, Func<ZoneCellViewModel, string> describe)
    {
        if (DataContext is not MonitorDiagramViewModel vm) return;
        if (context is not ZoneCellViewModel cell) return;

        vm.HoverHint = cell.IsInteractive ? describe(cell) : null;
    }

    public MonitorDiagram() => AvaloniaXamlLoader.Load(this);
}
