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
/// A measured length less a fixed inset, floored at zero.
/// <para>
/// Used to cap a chip at the tile's width minus its own margin. Capped at the
/// full width it can sit flush against the zone's border, which reads as having
/// burst out of it.
/// </para>
/// </summary>
public sealed class InsetConverter : IValueConverter
{
    public static readonly InsetConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double actual || double.IsNaN(actual)) return 0d;

        var inset = parameter switch
        {
            double d => d,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => 0,
        };

        return Math.Max(0, actual - inset);
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

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return false;
        if (values[0] is not double actual || double.IsNaN(actual)) return false;
        if (values[1] is not double threshold) return false;

        return actual >= threshold;
    }
}
