using Mullion.Core.Hotkeys;
using Mullion.Core.Model;

namespace Mullion.Core.Layout;

/// <summary>A whole-desk layout option, ranked for the wizard.</summary>
public sealed record LayoutCandidate(
    string Id,
    string Name,
    string Rationale,
    LayoutResult Layout,
    int Score,
    bool IsRecommended);

/// <summary>
/// Produces the alternatives the wizard offers.
/// <para>
/// The recommended layout is what the engine derives on its own; the rest apply
/// a uniform intent across every display. Presenting alternatives matters
/// because the scoring can find optima a person would not have chosen - so the
/// top-ranked option is pre-selected, never imposed.
/// </para>
/// </summary>
public static class LayoutCandidates
{
    public static IReadOnlyList<LayoutCandidate> Generate(
        IReadOnlyList<DisplayInfo> displays,
        KeySurface? surface = null,
        ShapeTuning? tuning = null,
        bool allowSpanningUnions = true)
    {
        if (displays.Count == 0) return [];

        var s = surface ?? KeySurface.LeftHandBlock;
        var t = tuning ?? ShapeTuning.Default;

        var candidates = new List<LayoutCandidate>
        {
            new("recommended",
                "Recommended",
                DescribeRecommended(displays, t),
                LayoutBuilder.Build(displays, s, t, allowSpanningUnions),
                100,
                true),
        };

        // Fewer, larger zones: one per display, no tiers.
        var simpleTuning = t with { ZoneAspectMax = 4.0, PreferredZoneAspect = 1.9 };
        candidates.Add(new LayoutCandidate(
            "simple",
            "One zone per display",
            "Each monitor is a single target. Fewest keys, largest windows.",
            LayoutBuilder.Build(displays, s, simpleTuning, allowSpanningUnions),
            60,
            false));

        // More, narrower zones.
        var denseTuning = t with { ZoneAspectMin = 0.45, PreferredZoneAspect = 0.85, MinZoneLogicalPx = 420 };
        candidates.Add(new LayoutCandidate(
            "dense",
            "More, narrower columns",
            "Splits each display further. Good for reference material side by side.",
            LayoutBuilder.Build(displays, s, denseTuning, allowSpanningUnions),
            55,
            false));

        // Home row only: no upper/lower halves.
        var noTiers = LayoutBuilder.Build(displays, s, t, allowSpanningUnions);
        var homeRowOnly = new LayoutResult(
            [.. noTiers.Zones.Where(z => z.Position.Row == s.HomeRow)],
            s,
            noTiers.Notes);

        candidates.Add(new LayoutCandidate(
            "home-row",
            "Home row only",
            "Just the main row. Leaves the rows above and below unbound.",
            homeRowOnly,
            50,
            false));

        return [.. candidates
            .GroupBy(c => Signature(c.Layout))
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .OrderByDescending(c => c.Score)];
    }

    /// <summary>Layouts that come out identical are collapsed, so the wizard never shows duplicates.</summary>
    private static string Signature(LayoutResult layout) =>
        string.Join('|', layout.Zones
            .OrderBy(z => z.Position.Row).ThenBy(z => z.Position.Col)
            .Select(z => $"{z.Position}:{string.Join(',', z.Parts.Select(p => $"{p.DisplayKey}{p.Area}"))}"));

    private static string DescribeRecommended(IReadOnlyList<DisplayInfo> displays, ShapeTuning tuning)
    {
        if (displays.Count == 1)
        {
            var only = displays[0];
            var counts = ShapeAnalyzer.ZoneCounts(only.Bounds, only.Dpi, false, tuning);
            var ratio = only.Elongation;

            return ratio >= 2.9
                ? $"A {ratio:0.#}:1 display, so a 16:9 center with side columns and a half above and below each."
                : ratio >= 2.1
                    ? $"A {ratio:0.#}:1 ultrawide, split into {counts.Preferred} with halves above and below."
                    : $"A single display, split into {counts.Preferred} with halves above and below.";
        }

        return $"{displays.Count} displays in physical order, each with a half above and below.";
    }
}
