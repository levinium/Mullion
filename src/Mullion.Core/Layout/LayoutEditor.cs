using Mullion.Core.Model;
using Mullion.Core.Hotkeys;

namespace Mullion.Core.Layout;

public sealed record RebindOutcome(bool Success, LayoutResult Layout, string Message);

/// <summary>Edits an existing layout without regenerating it.</summary>
public static class LayoutEditor
{
    /// <summary>
    /// Move the zone at <paramref name="source"/> onto <paramref name="target"/>,
    /// optionally with a modifier of its own.
    /// <para>
    /// Two zones collide only when their whole CHORD matches, not merely their
    /// key. Win+A and Ctrl+Alt+A are different hotkeys and can sit on the same
    /// key of the surface; treating the key alone as the identity would refuse
    /// half the bindings a person might reasonably want.
    /// </para>
    /// <para>
    /// When they do collide the two SWAP rather than one overwriting the other.
    /// Silently dropping a zone because its chord was taken would lose a target
    /// the user still wants and give no clue where it went.
    /// </para>
    /// </summary>
    public static RebindOutcome Rebind(
        LayoutResult layout,
        GridPos source,
        GridPos target,
        ChordModifiers? modifier = null,
        ChordModifiers fallback = ChordModifiers.Win)
    {
        if (!layout.Surface.Contains(target))
            return new RebindOutcome(false, layout, "That key is outside the current key surface.");

        var moving = layout.Zones.FirstOrDefault(z => z.Position == source);
        if (moving is null)
            return new RebindOutcome(false, layout, "That zone no longer exists.");

        var wanted = modifier ?? moving.Modifier;

        if (source == target && wanted == moving.Modifier)
            return new RebindOutcome(true, layout, "Unchanged.");

        var chord = wanted ?? fallback;

        var displaced = layout.Zones.FirstOrDefault(z =>
            !ReferenceEquals(z, moving) &&
            z.Position == target &&
            z.ChordWith(fallback) == chord);

        var zones = layout.Zones
            .Select(z =>
                ReferenceEquals(z, moving) ? z with { Position = target, Modifier = wanted }
                : displaced is not null && ReferenceEquals(z, displaced)
                    ? z with { Position = source, Modifier = moving.Modifier }
                : z)
            .ToList();

        var updated = new LayoutResult(zones, layout.Surface, layout.Notes);

        var targetLabel = Describe(chord, layout.Surface.FallbackLabelAt(target));
        var sourceLabel = Describe(moving.ChordWith(fallback), layout.Surface.FallbackLabelAt(source));

        var message = displaced is null
            ? $"{moving.Name} is now {targetLabel}."
            : $"Swapped — {moving.Name} is now {targetLabel}, and {displaced.Name} took {sourceLabel}.";

        return new RebindOutcome(true, updated, message);
    }

    private static string Describe(ChordModifiers mods, string key) =>
        $"{ModifierChoice.Format(mods)}+{key}";



    /// <summary>
    /// Whether two layouts put the same keys on the same zones.
    /// <para>
    /// Used to tell whether any key has been moved off where the allocator would
    /// have put it, by generating the layout again and comparing. That beats
    /// keeping a flag: a flag has to be set everywhere a key can move and
    /// cleared everywhere one can move back, and the first path that forgets
    /// leaves a "reset" button lying about whether it has anything to do.
    /// </para>
    /// <para>
    /// Zones are matched by the space they cover, because regenerating gives
    /// every zone a fresh identity. The shapes come out the same either way -
    /// only which key sits on which zone can differ.
    /// </para>
    /// </summary>
    public static bool SameKeyAssignments(LayoutResult a, LayoutResult b) =>
        Signature(a).SequenceEqual(Signature(b));

    private static IEnumerable<(string Shape, int Row, int Col, ChordModifiers? Mods)> Signature(
        LayoutResult layout) =>
        layout.Zones
            .Select(z => (Shape: ShapeOf(z), z.Position.Row, z.Position.Col, Mods: z.Modifier))
            .OrderBy(z => z.Shape, StringComparer.Ordinal)
            .ThenBy(z => z.Row)
            .ThenBy(z => z.Col);

    private static string ShapeOf(Zone zone) =>
        $"{zone.Kind}|" + string.Join(
            ';',
            zone.Parts
                .Select(p =>
                    $"{p.DisplayKey}:{p.Area.X:0.####},{p.Area.Y:0.####},{p.Area.W:0.####},{p.Area.H:0.####}")
                .OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>Find the grid cell a scan code corresponds to on this surface.</summary>
    public static GridPos? PositionOfScanCode(KeySurface surface, ushort scanCode)
    {
        foreach (var position in surface.Positions())
            if (surface.ScanCodeAt(position) == scanCode)
                return position;

        return null;
    }
}
