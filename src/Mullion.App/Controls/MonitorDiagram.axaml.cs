using Avalonia;
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

    public MonitorDiagram() => AvaloniaXamlLoader.Load(this);
}
