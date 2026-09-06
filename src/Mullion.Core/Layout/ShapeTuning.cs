namespace Mullion.Core.Layout;

/// <summary>
/// The four constants that govern how displays are subdivided. There is
/// deliberately no display-class enum: named tiers bake in arbitrary cutoffs
/// and break on unusual panels (3:2 laptops, 5:4 legacy monitors, whatever
/// ships next). Everything derives from these instead, and all four are
/// exposed in advanced settings so an odd panel is retuned, not special-cased.
/// </summary>
public sealed record ShapeTuning
{
    public static readonly ShapeTuning Default = new();

    /// <summary>Widest a zone may be before it reads as a letterbox strip.</summary>
    public double ZoneAspectMax { get; init; } = 2.20;

    /// <summary>Narrowest a zone may be before it reads as a slit.</summary>
    public double ZoneAspectMin { get; init; } = 0.62;

    /// <summary>
    /// Absolute floor on a zone's short side, in logical pixels, scaled by DPI.
    /// Catches what aspect ratio alone misses: a 1280x1024 panel split in two
    /// gives 640px columns, too narrow for real windows whatever the ratio says.
    /// </summary>
    public double MinZoneLogicalPx { get; init; } = 560;

    /// <summary>
    /// The zone shape preferred when choosing among viable split counts.
    /// A mild portrait - broadly the most useful window shape. This is a taste
    /// parameter, not a fact, which is why it is surfaced in settings.
    /// </summary>
    public double PreferredZoneAspect { get; init; } = 1.15;

    /// <summary>
    /// Weight given to a layout that contains a pane matching a canonical content
    /// ratio EXACTLY, scaled by how much of the display that pane covers.
    /// <para>
    /// This is a layout-level property, not a per-pane one, and that distinction
    /// matters: averaging a per-pane bonus lets three merely-decent panes outscore
    /// one perfect pane plus two side columns, which is precisely backwards.
    /// It is what makes 25/50/25 beat equal thirds on a 32:9.
    /// </para>
    /// </summary>
    public double ContentAnchorBonus { get; init; } = 1.5;

    /// <summary>How close a pane must be to a canonical ratio to count as "exact".</summary>
    public double ContentAnchorFitThreshold { get; init; } = 0.97;

    /// <summary>
    /// Ratios worth anchoring a pane to, with a prominence weight. 16:9 outranks
    /// 16:10 so a 32:9 resolves to 25/50/25 rather than the 27.5/45/27.5 that an
    /// unweighted 16:10 anchor would otherwise win with.
    /// </summary>
    /// <remarks>
    /// The weights are steeply graded because modern content is overwhelmingly
    /// 16:9. Flatter weights let the 4:3 anchor win on a 32:9 - it produces a
    /// 1920px center with 1600px sides that individually score well - which is
    /// not a layout anyone wants on a 5120-wide display.
    /// </remarks>
    public IReadOnlyList<CanonicalRatio> CanonicalRatios { get; init; } =
    [
        new(16.0 / 9.0, 1.00, "16:9"),
        new(16.0 / 10.0, 0.65, "16:10"),
        new(3.0 / 2.0, 0.50, "3:2"),
        new(4.0 / 3.0, 0.35, "4:3"),
    ];
}

/// <param name="Ratio">Long over short.</param>
/// <param name="Weight">Prominence; higher wins ties between anchors.</param>
/// <param name="Label">Display label, e.g. "16:9".</param>
public readonly record struct CanonicalRatio(double Ratio, double Weight, string Label);
