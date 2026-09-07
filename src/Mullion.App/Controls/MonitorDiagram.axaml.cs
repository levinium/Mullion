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

    public string ModifierPrefix
    {
        get => GetValue(ModifierPrefixProperty);
        set => SetValue(ModifierPrefixProperty, value);
    }

    public MonitorDiagram() => AvaloniaXamlLoader.Load(this);
}
