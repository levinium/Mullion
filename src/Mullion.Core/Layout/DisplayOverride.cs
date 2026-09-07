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
/// Relative sizes of those zones, or null to derive them. Normalised on use, so
/// [1,2,1] and [25,50,25] mean the same thing.
/// </param>
public sealed record DisplayOverride(
    string Slot,
    int? Columns = null,
    IReadOnlyList<double>? Weights = null)
{
    public bool IsEmpty => Columns is null && (Weights is null || Weights.Count == 0);
}

/// <summary>
/// Identifies a display by the place it occupies rather than by which monitor it
/// is.
/// <para>
/// Keying customisations to the EDID identity would throw them away when a
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
