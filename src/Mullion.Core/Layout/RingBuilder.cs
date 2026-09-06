using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Model;

namespace Mullion.Core.Layout;

/// <summary>One step of a key's cycle: a name and the area it targets.</summary>
public sealed record RingStep(string Name, IReadOnlyList<ZonePart> Parts);

/// <summary>
/// Builds the sequence a key walks through on repeated presses.
/// <para>
/// Cycling is gated on the modifier being held, not on a timer, so the ring
/// only advances while the user is still holding Win - releasing it resets to
/// the start. That makes a long ring safe: an accidental extra press cannot
/// leave the window somewhere unexpected minutes later.
/// </para>
/// </summary>
public static class RingBuilder
{
    /// <summary>
    /// Widen the zone step by step toward the whole display.
    /// <para>
    /// The steps are derived rather than configured: the zone itself, then the
    /// half of the display it sits in, then the display. Steps that duplicate
    /// an earlier one, or that a dedicated key already reaches, are dropped -
    /// every press should do something distinct, or the cycle feels broken.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RingStep> Build(
        Zone zone,
        LayoutResult layout,
        IReadOnlyList<DisplayInfo> displays)
    {
        // A zone spanning displays has no meaningful "wider" step short of the
        // whole desk, which is not a useful target.
        if (zone.SpansDisplays) return [new RingStep(zone.Name, zone.Parts)];

        var part = zone.Parts[0];
        var display = displays.FirstOrDefault(d => d.StableKey == part.DisplayKey);
        if (display is null) return [new RingStep(zone.Name, zone.Parts)];

        var steps = new List<RingStep> { new(zone.Name, zone.Parts) };
        var area = part.Area;

        // Keep the zone's vertical band: a key bound to the upper half should
        // widen within the upper half, not suddenly claim full height.
        var band = new NormRect(0, area.Y, 1, area.H);

        var centre = area.X + area.W / 2;
        var half = centre < 0.5
            ? new NormRect(0, band.Y, 0.5, band.H)
            : new NormRect(0.5, band.Y, 0.5, band.H);

        Add(half, centre < 0.5 ? "Left half" : "Right half");
        Add(band, band.H >= 0.999 ? "Whole display" : "Full width");

        return steps;

        void Add(NormRect candidate, string name)
        {
            const double Tolerance = 1e-4;

            // A ring only ever widens. Without this, a zone that already spans
            // the full width is offered "right half" as its next step, which
            // shrinks the window - the opposite of what repeat-pressing means.
            if (candidate.W * candidate.H <= area.W * area.H + Tolerance) return;

            var duplicate = steps.Any(s =>
                s.Parts.Count == 1 &&
                Math.Abs(s.Parts[0].Area.X - candidate.X) < Tolerance &&
                Math.Abs(s.Parts[0].Area.Y - candidate.Y) < Tolerance &&
                Math.Abs(s.Parts[0].Area.W - candidate.W) < Tolerance &&
                Math.Abs(s.Parts[0].Area.H - candidate.H) < Tolerance);

            if (duplicate) return;

            // Skip anything another key already reaches directly - the union
            // rule means a whole-column key often exists already.
            var reachable = layout.Zones.Any(z =>
                z.Position != zone.Position &&
                z.Parts.Count == 1 &&
                z.Parts[0].DisplayKey == part.DisplayKey &&
                Math.Abs(z.Parts[0].Area.X - candidate.X) < Tolerance &&
                Math.Abs(z.Parts[0].Area.Y - candidate.Y) < Tolerance &&
                Math.Abs(z.Parts[0].Area.W - candidate.W) < Tolerance &&
                Math.Abs(z.Parts[0].Area.H - candidate.H) < Tolerance);

            if (reachable) return;

            steps.Add(new RingStep($"{zone.Name} → {name}", [new ZonePart(part.DisplayKey, candidate)]));
        }
    }
}
