using Mullion.Core.Geometry;

namespace Mullion.Core.Layout;

/// <summary>
/// Which way a zone's two subzone keys cut it.
/// <para>
/// Tiers used to be stacked always, which quietly broke the one rule the rest of
/// the layout engine obeys: a zone has to be a shape a window can live in.
/// Halving a 16:9 monitor into 1920x540 strips produces an aspect of 3.56 against
/// a ZoneAspectMax of 2.20 - a pair of letterboxes the engine would have refused
/// outright had they been zones. Splitting the same monitor side by side gives
/// 960x1080, or 0.89, which is comfortably inside the band.
/// </para>
/// <para>
/// So the axis is derived rather than fixed, by the rule already used for zone
/// counts and split weights. This is deliberately per zone and not a setting:
/// on a single 5120x1440 the correct answer differs WITHIN one desk. The centre
/// zone, 2560x1392, stacks to 3.68 (out) and splits to 0.92 (in); the side zones,
/// 1280x1392, stack to 1.84 (in) and split to 0.46 (out). Any global switch is
/// wrong for one of them.
/// </para>
/// </summary>
public static class TierAxis
{
    /// <summary>Slack for float noise, in log-aspect units.</summary>
    private const double Epsilon = 1e-9;

    /// <summary>
    /// How much worse an unusably narrow half is than a merely mis-shaped one.
    /// Large enough to dominate any aspect miss, since a half no window fits in
    /// is not a trade-off - it is a subzone that cannot be used at all.
    /// </summary>
    private const double TooSmall = 100.0;

    /// <summary>
    /// The axis whose halves make the better pair of windows.
    /// <para>
    /// Ties resolve to Vertical, which is what tiers have always been, so a zone
    /// with no real preference keeps the arrangement people already learned.
    /// </para>
    /// </summary>
    /// <param name="widthPx">The zone's width in physical pixels.</param>
    /// <param name="heightPx">The zone's height in physical pixels.</param>
    /// <param name="minShortSidePx">
    /// Floor on a half's short side, DPI-scaled by the caller - the same floor
    /// that stops a 1280x1024 panel being split into 640px columns.
    /// </param>
    public static Axis For(double widthPx, double heightPx, double minShortSidePx, ShapeTuning t)
    {
        var stacked = Judge(widthPx, heightPx / 2, minShortSidePx, t);
        var sideBySide = Judge(widthPx / 2, heightPx, minShortSidePx, t);

        // One usable and one not is the common case, and the only one where the
        // answer is forced rather than chosen.
        if (stacked.Usable != sideBySide.Usable)
            return stacked.Usable ? Axis.Vertical : Axis.Horizontal;

        // Both usable: the better-shaped pair wins. Both unusable: the less bad
        // one does, so an awkward display still gets the more sensible of two
        // poor answers rather than the stacked one by default.
        var (stackedCost, sideCost) = stacked.Usable
            ? (stacked.FromPreferred, sideBySide.FromPreferred)
            : (stacked.Miss, sideBySide.Miss);

        return sideCost < stackedCost - Epsilon ? Axis.Horizontal : Axis.Vertical;
    }

    /// <summary>
    /// Convenience for the common caller: a zone expressed as a fraction of a
    /// display's work area.
    /// </summary>
    public static Axis For(NormRect area, PxRect workArea, uint dpi, ShapeTuning t) =>
        For(
            area.W * workArea.Width,
            area.H * workArea.Height,
            t.MinZoneLogicalPx * dpi / 96.0,
            t);

    private readonly record struct Judgement(bool Usable, double Miss, double FromPreferred);

    /// <summary>
    /// Aspect here is width over height, matching ZoneAspectMin/Max - 0.62 is a
    /// tall slit and 2.20 a wide letterbox, so this is not the orientation-
    /// independent long/short ratio used for classifying whole displays.
    /// <para>
    /// Distances are measured in log space because aspect ratios are
    /// multiplicative: 4.0 is as far above 2.0 as 1.0 is below it, which a plain
    /// subtraction gets wrong in exactly the direction that matters here.
    /// </para>
    /// </summary>
    private static Judgement Judge(double w, double h, double minShortSidePx, ShapeTuning t)
    {
        if (w <= 0 || h <= 0) return new Judgement(false, double.PositiveInfinity, double.PositiveInfinity);

        var aspect = w / h;

        var miss =
            aspect > t.ZoneAspectMax ? Math.Log(aspect / t.ZoneAspectMax) :
            aspect < t.ZoneAspectMin ? Math.Log(t.ZoneAspectMin / aspect) :
            0.0;

        var fits = Math.Min(w, h) >= minShortSidePx;
        if (!fits) miss += TooSmall;

        return new Judgement(
            miss == 0.0,
            miss,
            Math.Abs(Math.Log(aspect / t.PreferredZoneAspect)));
    }
}
