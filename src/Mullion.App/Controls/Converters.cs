using System.Globalization;
using Avalonia.Data.Converters;

namespace Mullion.App.Controls;

/// <summary>
/// True when a measured length is at least the given threshold.
/// <para>
/// Used to drop secondary text out of a zone that has become too small to hold
/// it. The diagram scales to fit, so how much room a zone has is not known until
/// it is arranged - a narrow portrait monitor and a 32:9 draw the same zones at
/// wildly different sizes. Deciding from the measured width means the detail
/// disappears rather than being clipped mid-word, and it adapts on its own to a
/// longer modifier, a bigger font, or a smaller window.
/// </para>
/// </summary>
public sealed class WiderThanConverter : IValueConverter
{
    public static readonly WiderThanConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double actual || double.IsNaN(actual)) return false;

        var threshold = parameter switch
        {
            double d => d,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => 0,
        };

        return actual >= threshold;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A measured length less a fixed inset, never below a floor.
/// <para>
/// Used to cap a chip at the tile's width minus its own margin: capped at the
/// full width it sits flush against the zone's border, which reads as having
/// burst out of it.
/// </para>
/// <para>
/// The floor is what stops the cap becoming a lie. A chord is drawn as separate
/// runs so it can wrap between them, and a run squeezed below its own width is
/// not wrapped but CLIPPED - the trailing "+" simply disappears. Below the floor
/// the chip stops shrinking and the Viewbox around it scales instead, which
/// costs legibility rather than correctness.
/// </para>
/// <para>
/// Parameter is "inset" or "inset;floor".
/// </para>
/// </summary>
public sealed class InsetConverter : IValueConverter
{
    public static readonly InsetConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double actual || double.IsNaN(actual)) return 0d;

        var (inset, floor) = Parse(parameter);

        return Math.Max(floor, actual - inset);
    }

    private static (double Inset, double Floor) Parse(object? parameter)
    {
        if (parameter is double d) return (d, 0);
        if (parameter is not string text) return (0, 0);

        var parts = text.Split(';', StringSplitOptions.TrimEntries);

        var inset = parts.Length > 0 && double.TryParse(
            parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : 0;

        var floor = parts.Length > 1 && double.TryParse(
            parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 0;

        return (inset, floor);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True when a measured length is at least a threshold supplied by a second
/// binding.
/// <para>
/// Two bindings rather than a converter parameter because the threshold is not
/// a constant: how tall a zone must be before its name will fit depends on what
/// else that zone is carrying. A zone with tier chips at its quarter marks has
/// far less room in the middle than one without.
/// </para>
/// </summary>
public sealed class AtLeastConverter : IMultiValueConverter
{
    public static readonly AtLeastConverter Instance = new();

    /// <summary>Height of a key chip whose chord fits on one line.</summary>
    private const double SingleLineChip = 24;

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return false;
        if (values[0] is not double actual || double.IsNaN(actual)) return false;
        if (values[1] is not double threshold) return false;

        // An optional third value is space already spoken for above this one -
        // in practice the key chip, whose height is not a constant since a long
        // chord wraps to two or three lines. The thresholds are tuned against a
        // single-line chip, so only the growth beyond that is charged. Without
        // this the name shows on a tile the wrapped chip has already filled, and
        // lands on the zone's own border.
        if (values.Count > 2 && values[2] is double occupied && !double.IsNaN(occupied))
            threshold += Math.Max(0, occupied - SingleLineChip);

        return actual >= threshold;
    }
}

/// <summary>
/// How tall a zone's key chip may grow: the smaller of what the chord is allowed
/// and what the tile actually has spare between its tier chips.
/// <para>
/// The allowance alone was a constant, which is right for a diagram whose tiles
/// are hundreds of pixels tall and wrong for a wizard card, where a tile is
/// about fifty. The chip took the 24px it was permitted, the tier chips sat at
/// the quarter marks either side, and all three overlapped - three key labels in
/// the same place, which is the one thing the card is choosing between.
/// </para>
/// <para>
/// The tier's own rendered height is a binding rather than a number because it
/// is not one: the chips are smaller in thumbnail mode than in the full diagram,
/// and a constant tuned for either is wrong for the other.
/// </para>
/// </summary>
public sealed class ChipHeightConverter : IMultiValueConverter
{
    public static readonly ChipHeightConverter Instance = new();

    /// <summary>
    /// Clear air between the chip and a tier chip beside it.
    /// <para>
    /// Four, not one: the chip is centered on the tile and the tier chips on
    /// the halves of a grid inset from it, so the two centers differ by a
    /// pixel or so and a cap that only just fits still lands on a tier.
    /// </para>
    /// </summary>
    private const double Breathing = 4;

    /// <summary>
    /// Never returns nothing. A chip shrunk to zero is not a smaller label, it
    /// is a missing one, and a tile too short for any of this drops the chip
    /// outright elsewhere rather than here.
    /// </summary>
    private const double Floor = 8;

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return double.PositiveInfinity;
        if (values[0] is not double tile || double.IsNaN(tile)) return double.PositiveInfinity;
        if (values[1] is not double allowed || double.IsNaN(allowed)) return double.PositiveInfinity;

        var tier = values.Count > 2 && values[2] is double t && !double.IsNaN(t) ? t : 0;
        var inset = values.Count > 3 && values[3] is double i && !double.IsNaN(i) ? i : 0;

        // Where the tier chips actually are, rather than roughly. They sit
        // centered in the halves of a grid inset from the tile, so the upper
        // one's lower edge is at inset + (tile - 2*inset)/4 + tier/2, the chip
        // is centered on the tile, and twice the distance between the two is
        // all it has. Reasoning from the tile alone overstates that by the
        // whole inset - which on a card is most of the band.
        var band = tier > 0 ? tile / 2 - inset - tier - Breathing : tile;

        return Math.Max(Floor, Math.Min(allowed, band));
    }
}

/// <summary>One step of a seam drag.</summary>
/// <param name="Index">Which seam.</param>
/// <param name="Position">Where it has been dragged to, as a fraction of the display.</param>
/// <param name="Fine">
/// Snapping suspended for this move, because a modifier is held. A grid that
/// cannot be escaped is worse than no grid: the one position someone wants is
/// always the one between two stops.
/// </param>
public readonly record struct SeamDrag(int Index, double Position, bool Fine);

/// <summary>One run of a chord, and whether it is a modifier or the key itself.</summary>
public sealed record ChordPart(string Text, bool IsModifier);

/// <summary>
/// A chord as the runs it may be broken between: "Ctrl+", "Shift+", "Q".
/// <para>
/// The modifier runs come from the diagram and the key from the zone, but they
/// have to end up as siblings in one panel: wrapping only happens between a
/// panel's own children, so a modifier nested in its own ItemsControl can never
/// break onto the same line as the key. Joining them here is what lets
/// "Ctrl+Shift+Q" wrap as three runs instead of two things that each wrap alone.
/// </para>
/// </summary>
public sealed class ChordPartsConverter : IMultiValueConverter
{
    public static readonly ChordPartsConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = new List<ChordPart>();

        if (values.Count > 0 && values[0] is IEnumerable<string> segments)
            parts.AddRange(segments.Select(s => new ChordPart(s, true)));

        if (values.Count > 1 && values[1] is string key && key.Length > 0)
            parts.Add(new ChordPart(key, false));

        return parts;
    }
}

/// <summary>
/// A measured length, less a reservation, but only once it is wide enough to be
/// worth reserving from.
/// <para>
/// Two things share the band above a display: its name on the left and the zone
/// stepper on the right. On a wide monitor both fit and the name simply has to
/// stop short of the stepper. On a narrow one - a portrait panel drawn 55px
/// across - nothing fits beside anything, so the stepper is not drawn there at
/// all and the name gets the whole width back rather than being trimmed to
/// nothing to leave room for something absent.
/// </para>
/// <para>
/// Parameter is "threshold;reserve".
/// </para>
/// </summary>
public sealed class ReserveWhenWideConverter : IValueConverter
{
    public static readonly ReserveWhenWideConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double actual || double.IsNaN(actual)) return 0d;

        var parts = (parameter as string ?? string.Empty).Split(';', StringSplitOptions.TrimEntries);

        var threshold = Read(parts, 0);
        var reserve = Read(parts, 1);

        return actual >= threshold ? Math.Max(0, actual - reserve) : actual;
    }

    private static double Read(string[] parts, int index) =>
        parts.Length > index && double.TryParse(
            parts[index], NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Whether a display is tall enough on screen to carry a name tab inside it.
/// <para>
/// The tab that sits ON a display - the one used where another display is
/// stacked directly above and there is no headroom - is drawn over the zones
/// rather than over empty space. On a display rendered short, the zone's own key
/// chip is centered in what is also the top-left corner, and the tab printed
/// across it: "Win+X" showing as "X" on a stacked pair.
/// </para>
/// <para>
/// So the tab yields. The chip is the whole point of the diagram and must never
/// be obscured; the name is context, it is still in the tooltip, and for a
/// whole-display zone the zone's own label repeats it directly underneath.
/// </para>
/// <para>
/// The vertical twin of <see cref="WiderThanConverter"/>, and deliberately a
/// separate type rather than a reuse of it: the call site binds a Height, and a
/// converter named "wider than" sitting on one reads as a mistake every time
/// anybody looks at it.
/// </para>
/// </summary>
public sealed class ShowWhenTallerThanConverter : IValueConverter
{
    public static readonly ShowWhenTallerThanConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double actual || double.IsNaN(actual)) return false;

        var threshold = double.TryParse(
            parameter as string, NumberStyles.Any, CultureInfo.InvariantCulture, out var t) ? t : 0;

        return actual >= threshold;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// "Edit zones" or "Done", for the button that toggles editing.
/// <para>
/// The label has to say what pressing it will do, and both halves of that are
/// one word apart, so a converter beats two buttons swapping visibility.
/// </para>
/// </summary>
public sealed class EditLabelConverter : IValueConverter
{
    public static readonly EditLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Done editing zones" : "Edit zones";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Whether a side-by-side subzone chip can sit on its half's middle line, or has
/// to tuck into the row above it.
/// <para>
/// Centring it there is the arrangement that reads correctly - a chip naming the
/// left half belongs in the middle of the left half. What stands in the way is
/// the whole-zone block, which owns the middle of the tile and is the widest
/// thing in it, carrying a chord, a name and a size. Whether the three fit
/// across one line is not a property of the layout but of the words in them: with
/// "Win+" chords a 499px tile has room to spare, while the same tile with
/// "Ctrl+Shift+" chords does not. So it is asked of the arranged widths rather
/// than answered once with a constant, which is what an earlier version did -
/// and it took the worst case for the only case, so every tile lost the centring
/// to spare the few that could not afford it.
/// </para>
/// <para>
/// Returns a Grid.RowSpan: 2 spans both rows and centres on the tile, 1 keeps the
/// chip in its own row. Expressed that way because the span is the only thing
/// that has to change - the column, and so the horizontal placement, is the same
/// either way.
/// </para>
/// </summary>
public sealed class CentredChipFitsConverter : IMultiValueConverter
{
    public static readonly CentredChipFitsConverter Instance = new();

    /// <summary>Clear air between the chip and the block, so they read as separate.</summary>
    private const double Breathing = 10;

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Stacked halves already have a row each, well clear of the middle.
        if (values.Count < 4 || values[0] is not true) return 1;

        if (values[1] is not double tile || double.IsNaN(tile)) return 1;
        if (values[2] is not double block || double.IsNaN(block)) return 1;
        if (values[3] is not double chip || double.IsNaN(chip)) return 1;

        // Nothing arranged yet: keep the safe arrangement rather than flicker
        // into the centred one and back out on the next pass.
        if (tile <= 0 || chip <= 0) return 1;

        // The chip is centred in its half, so its centre is a quarter of the
        // tile from the middle; the block is centred on the middle. Halves of
        // each, because both spread either side of their own centre.
        return tile / 4 >= (block / 2) + (chip / 2) + Breathing ? 2 : 1;
    }
}
