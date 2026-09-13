using Mullion.Core.Geometry;
using Mullion.Core.Model;

namespace Mullion.Core.Layout;

/// <summary>
/// A hand-made change to one display's split, replacing what the analyzer would
/// have chosen.
/// </summary>
/// <param name="Slot">
/// Which display this belongs to. See <see cref="DisplaySlot"/>: it names a
/// place on the desk, not a monitor.
/// </param>
/// <param name="Columns">How many zones across (or down, on a portrait display), or null to derive it.</param>
/// <param name="Weights">
/// Relative sizes of those zones, or null to derive them. Normalized on use, so
/// [1,2,1] and [25,50,25] mean the same thing.
/// </param>
/// <param name="SubzoneAxes">
/// Per zone of this display, which way that zone's two subzones cut it, or null
/// in a slot to derive it from the zone's shape. Same shape and same ordering as
/// <paramref name="Weights"/>.
/// <para>
/// Words rather than an enum, so the file stays readable and a value written by
/// a newer build degrades to "derive it" instead of failing the whole load. Which
/// way is a preference as much as a measurement - a zone can be the right shape
/// for side-by-side halves and still be somewhere you always want one window
/// above another.
/// </para>
/// </param>
public sealed record DisplayOverride(
    string Slot,
    int? Columns = null,
    IReadOnlyList<double>? Weights = null,
    IReadOnlyList<string?>? SubzoneAxes = null)
{
    /// <summary>The word stored for halves one above the other.</summary>
    public const string Stacked = "stacked";

    /// <summary>The word stored for halves beside each other.</summary>
    public const string SideBySide = "side-by-side";

    public bool IsEmpty =>
        Columns is null &&
        (Weights is null || Weights.Count == 0) &&
        (SubzoneAxes is null || SubzoneAxes.All(a => Parse(a) is null));

    /// <summary>
    /// The axis chosen by hand for one of this display's zones, or null to let
    /// the shape decide. Out-of-range and unrecognised entries answer null, so a
    /// zone count that changed since these were saved cannot pin the wrong zone.
    /// </summary>
    public Axis? AxisFor(int zone) =>
        SubzoneAxes is null || zone < 0 || zone >= SubzoneAxes.Count
            ? null
            : Parse(SubzoneAxes[zone]);

    private static Axis? Parse(string? word) => word switch
    {
        Stacked => Axis.Vertical,
        SideBySide => Axis.Horizontal,
        _ => null,
    };

    /// <summary>The word for an axis, or null for "derive it".</summary>
    public static string? Word(Axis? axis) => axis switch
    {
        Axis.Vertical => Stacked,
        Axis.Horizontal => SideBySide,
        _ => null,
    };
}

/// <summary>
/// Identifies a display by the place it occupies rather than by which monitor it
/// is.
/// <para>
/// Keying customizations to the EDID identity would throw them away when a
/// monitor is replaced by an identical one, or when a dock hands back the same
/// desk through different hardware - and the zones are a statement about the
/// desk, not about the panel. Two monitors of the same size in the same position
/// take the same zones, so they are the same slot.
/// </para>
/// <para>
/// Size and origin only. Not DPI: zones are stored as fractions, so a scaling
/// change does not alter them. Not the work area: a taskbar that moves or
/// auto-hides must not discard a layout somebody built by hand.
/// </para>
/// </summary>
public static class DisplaySlot
{
    public static string Of(DisplayInfo display) =>
        $"{display.Bounds.Width}x{display.Bounds.Height}@{display.Bounds.X},{display.Bounds.Y}";
}
