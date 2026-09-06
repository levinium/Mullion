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

    /// <summary>True when the zone crosses a physical bezel.</summary>
    public bool SpansDisplays => Parts.Select(p => p.DisplayKey).Distinct().Count() > 1;

    public override string ToString() => $"{Position} {Name}";
}
