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

        // By the key each ends up answering to. A zone can be sitting at the "A"
        // cell and bound to F5, and saying "A" then names the wrong key.
        var targetLabel = Describe(chord, KeyNames.Of(KeyAt(layout, target)));
        var sourceLabel = Describe(moving.ChordWith(fallback), KeyNames.Of(KeyAt(layout, source)));

        var message = displaced is null
            ? $"{moving.Name} is now {targetLabel}."
            : $"Swapped — {moving.Name} is now {targetLabel}, and {displaced.Name} took {sourceLabel}.";

        return new RebindOutcome(true, updated, message);
    }

    /// <summary>The key whatever sits at this cell actually answers to.</summary>
    private static KeyStroke KeyAt(LayoutResult layout, GridPos at) =>
        layout.Zones.FirstOrDefault(z => z.Position == at) is { } zone
            ? zone.KeyOn(layout.Surface)
            : KeyStroke.Plain(layout.Surface.Contains(at) ? layout.Surface.ScanCodeAt(at) : (ushort)0);

    private static string Describe(ChordModifiers mods, string key) =>
        $"{ModifierChoice.Format(mods)}+{key}";

    /// <summary>
    /// Put a zone on a key that is not part of the key surface.
    /// <para>
    /// The surface is a set of DEFAULTS - a shape the allocator lays zones out
    /// on - and there was never a reason it should also be a cage. Rebinding
    /// used to refuse anything outside it, so a zone could not be moved to F5,
    /// to an arrow, or to the tilde key, and the only way to reach those was to
    /// switch the whole desk to a different surface.
    /// </para>
    /// <para>
    /// The zone keeps its position: that is where it sits on the desk, which the
    /// diagram draws and which identifies it across a rebuild. Only the key it
    /// answers to changes. A zone already on that chord swaps keys with it, the
    /// same as an on-surface rebind, so nothing is silently displaced.
    /// </para>
    /// </summary>
    public static RebindOutcome RebindToKey(
        LayoutResult layout,
        GridPos source,
        KeyStroke key,
        ChordModifiers? modifier = null,
        ChordModifiers fallback = ChordModifiers.Win)
    {
        if (!KeyNames.IsBindable(key))
            return new RebindOutcome(false, layout, "That key cannot be used for a hotkey.");

        var moving = layout.Zones.FirstOrDefault(z => z.Position == source);
        if (moving is null) return new RebindOutcome(false, layout, "That zone no longer exists.");

        var wanted = modifier ?? moving.Modifier;
        var chord = wanted ?? fallback;

        // What this zone answers to now, so a swap has something to give away.
        var vacated = moving.KeyOn(layout.Surface);

        if (vacated == key && wanted == moving.Modifier)
            return new RebindOutcome(true, layout, "Unchanged.");

        var displaced = layout.Zones.FirstOrDefault(z =>
            !ReferenceEquals(z, moving) &&
            z.KeyOn(layout.Surface) == key &&
            z.ChordWith(fallback) == chord);

        var zones = layout.Zones
            .Select(z =>
                ReferenceEquals(z, moving)
                    ? z with { Key = Settle(z, key, layout.Surface), Modifier = wanted }
                : displaced is not null && ReferenceEquals(z, displaced)
                    ? z with { Key = Settle(z, vacated, layout.Surface), Modifier = moving.Modifier }
                : z)
            .ToList();

        var updated = new LayoutResult(zones, layout.Surface, layout.Notes);

        var message = displaced is null
            ? $"{moving.Name} is now {Describe(chord, KeyNames.Of(key))}."
            : $"Swapped — {moving.Name} is now {Describe(chord, KeyNames.Of(key))}, " +
              $"and {displaced.Name} took {Describe(displaced.ChordWith(fallback), KeyNames.Of(vacated))}.";

        return new RebindOutcome(true, updated, message);
    }

    /// <summary>
    /// Store a key only when it differs from the one the zone's place implies.
    /// <para>
    /// Otherwise a zone put back on its own key would keep an override saying so,
    /// and then stop following a change of key surface for no reason anyone could
    /// see - the same rule the modifier already follows.
    /// </para>
    /// </summary>
    private static KeyStroke? Settle(Zone zone, KeyStroke key, KeySurface surface) =>
        surface.Contains(zone.Position) && KeyStroke.Plain(surface.ScanCodeAt(zone.Position)) == key
            ? null
            : key;



    /// <summary>
    /// A freshly generated layout with the keys someone chose put back on it.
    /// <para>
    /// The layout is rebuilt from scratch whenever anything about the desk
    /// changes - a zone count, a dragged seam, an undo, a new monitor - and the
    /// builder is given displays, a surface and zone shapes. It has never been
    /// told which chord a zone was bound to, so every rebuild handed back the
    /// allocator's own answer and a rebind lasted until the next thing you did.
    /// </para>
    /// <para>
    /// Two rules, because two things can be carried and they are not equally
    /// safe. A MODIFIER belongs to one zone and moving it can collide with
    /// nothing, so it comes across wherever its zone still exists. A POSITION
    /// is a claim on a key that some other zone may now hold, so it is carried
    /// only when the zone sets match exactly - which is the case for a seam
    /// drag or a re-detect, and is not the case when the count changed and the
    /// old assignment was a bijection over a set that no longer exists.
    /// </para>
    /// </summary>
    public static LayoutResult CarryKeysOver(LayoutResult fresh, LayoutResult? previous)
    {
        if (previous is null) return fresh;

        var chosen = previous.Zones.ToDictionary(IdentityOf, z => z, StringComparer.Ordinal);
        if (chosen.Count != previous.Zones.Count) return fresh;

        var sameZones =
            fresh.Zones.Count == previous.Zones.Count &&
            fresh.Zones.All(z => chosen.ContainsKey(IdentityOf(z)));

        var zones = fresh.Zones
            .Select(z =>
            {
                if (!chosen.TryGetValue(IdentityOf(z), out var was)) return z;

                return sameZones
                    ? z with { Position = was.Position, Modifier = was.Modifier }
                    : z with { Modifier = was.Modifier };
            })
            .ToList();

        return new LayoutResult(zones, fresh.Surface, fresh.Notes);
    }

    /// <summary>
    /// What makes a zone the same zone after the layout has been rebuilt.
    /// <para>
    /// Not its shape, which is what <see cref="SameKeyAssignments"/> uses:
    /// dragging a seam changes every shape it touches, and that is exactly the
    /// moment a rebind must not be lost. The name survives it, and the display
    /// keys are there because two identical monitors get the same friendly name
    /// and so name their zones identically.
    /// </para>
    /// </summary>
    private static string IdentityOf(Zone zone) =>
        $"{Role(zone.Name)}|" + string.Join(
            ';', zone.Parts.Select(p => p.DisplayKey).OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>
    /// The name with its subzone direction reduced to a slot.
    /// <para>
    /// Which way a zone's halves are cut is derived from its shape, so a seam
    /// drag can rename "Center upper" to "Center left" - and the name was the one
    /// thing identity relied on surviving a resize. Without this, widening a
    /// column silently drops the hand-bound key off both its halves, which is the
    /// exact failure the name-based identity was introduced to prevent.
    /// </para>
    /// <para>
    /// Reduced from the name rather than carried as a field on the zone, because
    /// identity has to hold for a layout restored from a profile too, and a
    /// profile stores the name.
    /// </para>
    /// </summary>
    private static string Role(string name)
    {
        foreach (var (word, slot) in Halves)
        {
            if (!name.EndsWith(word, StringComparison.Ordinal)) continue;

            return string.Concat(name.AsSpan(0, name.Length - word.Length), slot);
        }

        return name;
    }

    /// <summary>
    /// The direction words tiers are named with, paired with the slot they mean.
    /// The key above home always takes the first half, the key below the second.
    /// </summary>
    private static readonly (string Word, string Slot)[] Halves =
    [
        (" upper", " #1"),
        (" left", " #1"),
        (" lower", " #2"),
        (" right", " #2"),
    ];

    /// <summary>
    /// Whether two layouts carve the desk into the same zones, whatever keys
    /// are on them.
    /// <para>
    /// The shape counterpart to <see cref="SameKeyAssignments"/>, and it exists
    /// for the same reason: "has anything been customized" is a question worth
    /// answering by looking, not by keeping a flag. The flag version counted
    /// override RECORDS, so a stored override saying "three columns" on a
    /// display the engine already derives three columns for left the reset
    /// button lit up with nothing to undo.
    /// </para>
    /// </summary>
    public static bool SameZoneShapes(LayoutResult a, LayoutResult b) =>
        a.Zones.Select(ShapeOf).OrderBy(s => s, StringComparer.Ordinal)
            .SequenceEqual(b.Zones.Select(ShapeOf).OrderBy(s => s, StringComparer.Ordinal));

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
