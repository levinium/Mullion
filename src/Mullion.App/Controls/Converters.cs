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
