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
