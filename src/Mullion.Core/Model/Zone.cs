using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;

namespace Mullion.Core.Model;

/// <summary>
/// One display's contribution to a zone, as a fraction of that display's WORK
/// AREA. Fractions rather than pixels so zones survive resolution, DPI and
/// taskbar changes.
/// </summary>
public sealed record ZonePart(string DisplayKey, NormRect Area);

/// <summary>What a zone represents, which drives naming and the wizard's labelling.</summary>
public enum ZoneKind
{
    /// <summary>A region within one display.</summary>
    Region,

    /// <summary>An entire display.</summary>
    WholeDisplay,

    /// <summary>The union of a column's zones - "the whole of this column".</summary>
    Union,
}

/// <summary>
/// A snap target. Usually one part on one display; a union zone spanning two
/// stacked monitors carries a part per display, and the mover takes the bounding
/// box of the projected parts.
/// </summary>
public sealed record Zone
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<ZonePart> Parts { get; init; }
    public required GridPos Position { get; init; }
    public ZoneKind Kind { get; init; } = ZoneKind.Region;

    /// <summary>
    /// The modifier this zone is taken with, when it is not the configured
    /// default.
    /// <para>
    /// Null means "whatever the default is", so changing that setting moves
    /// every zone that has not been given one of its own - which is what a
    /// default is for. A zone that HAS been bound by hand keeps its chord,
    /// because the point of binding it by hand was to choose.
    /// </para>
    /// </summary>
    public Hotkeys.ChordModifiers? Modifier { get; init; }

    /// <summary>The chord this zone actually answers to, given a default.</summary>
    public Hotkeys.ChordModifiers ChordWith(Hotkeys.ChordModifiers fallback) => Modifier ?? fallback;

    /// <summary>
    /// The physical key this zone answers to, when it is not the one its place
    /// on the surface implies.
    /// <para>
    /// The same arrangement as <see cref="Modifier"/>, and for the same reason.
    /// Position says where a zone sits in the grid - which is what the diagram
    /// draws and what identifies the zone across a rebuild - while this says
    /// which key reaches it. They were the same thing for as long as a binding
    /// could only land on the key surface, so a zone could be moved to F5 or to
    /// the tilde key only by not being movable there at all.
    /// </para>
    /// <para>
    /// Null means "whatever my position implies", so changing the key surface
    /// still moves every zone that has not been given a key of its own.
    /// </para>
    /// </summary>
    public Hotkeys.KeyStroke? Key { get; init; }

    /// <summary>The key this zone answers to, given the surface it sits on.</summary>
    public Hotkeys.KeyStroke KeyOn(Hotkeys.KeySurface surface) =>
        Key ?? Hotkeys.KeyStroke.Plain(surface.ScanCodeAt(Position));

    /// <summary>True when the zone crosses a physical bezel.</summary>
    public bool SpansDisplays => Parts.Select(p => p.DisplayKey).Distinct().Count() > 1;

    public override string ToString() => $"{Position} {Name}";
}
