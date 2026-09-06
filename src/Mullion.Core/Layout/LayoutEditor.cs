using Mullion.Core.Hotkeys;

namespace Mullion.Core.Layout;

public sealed record RebindOutcome(bool Success, LayoutResult Layout, string Message);

/// <summary>Edits an existing layout without regenerating it.</summary>
public static class LayoutEditor
{
    /// <summary>
    /// Move the zone at <paramref name="source"/> onto <paramref name="target"/>.
    /// <para>
    /// If another zone already holds the target key the two SWAP rather than one
    /// overwriting the other. Silently dropping a zone because its key was taken
    /// would lose a target the user still wants and give no clue where it went.
    /// </para>
    /// </summary>
    public static RebindOutcome Rebind(LayoutResult layout, GridPos source, GridPos target)
    {
        if (!layout.Surface.Contains(target))
            return new RebindOutcome(false, layout, "That key is outside the current key surface.");

        var moving = layout.Zones.FirstOrDefault(z => z.Position == source);
        if (moving is null)
            return new RebindOutcome(false, layout, "That zone no longer exists.");

        if (source == target)
            return new RebindOutcome(true, layout, "Unchanged.");

        var displaced = layout.Zones.FirstOrDefault(z => z.Position == target);

        var zones = layout.Zones
            .Select(z =>
                ReferenceEquals(z, moving) ? z with { Position = target }
                : displaced is not null && ReferenceEquals(z, displaced) ? z with { Position = source }
                : z)
            .ToList();

        var updated = new LayoutResult(zones, layout.Surface, layout.Notes);

        var targetLabel = layout.Surface.FallbackLabelAt(target);
        var sourceLabel = layout.Surface.FallbackLabelAt(source);

        var message = displaced is null
            ? $"{moving.Name} is now Win+{targetLabel}."
            : $"Swapped — {moving.Name} is now Win+{targetLabel}, and {displaced.Name} took Win+{sourceLabel}.";

        return new RebindOutcome(true, updated, message);
    }

    /// <summary>Find the grid cell a scan code corresponds to on this surface.</summary>
    public static GridPos? PositionOfScanCode(KeySurface surface, ushort scanCode)
    {
        foreach (var position in surface.Positions())
            if (surface.ScanCodeAt(position) == scanCode)
                return position;

        return null;
    }
}
