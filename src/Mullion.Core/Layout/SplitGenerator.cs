using Mullion.Core.Geometry;

namespace Mullion.Core.Layout;

/// <summary>One way of dividing a display, with the score that ranks it.</summary>
public sealed record SplitCandidate(
    string Id,
    string Name,
    IReadOnlyList<double> Weights,
    double Score,
    string Rationale)
{
    public int Count => Weights.Count;
}

/// <summary>
/// Generates and ranks ways to divide a display along its long axis.
/// <para>
/// Split proportions are SCORED, not named. There is no
/// <c>if (superUltrawide) use25_50_25</c> branch anywhere: the 25/50/25 that a
/// 32:9 resolves to is simply the highest-scoring candidate, and the equal
/// thirds a 21:9 resolves to wins there on the same scoring.
/// </para>
/// </summary>
public static class SplitGenerator
{
    /// <summary>Candidates for a given split count, best first.</summary>
    public static IReadOnlyList<SplitCandidate> Candidates(
        PxRect bounds, int count, ShapeTuning? tuning = null)
    {
        var t = tuning ?? ShapeTuning.Default;
        if (count <= 1)
        {
            return [new SplitCandidate("whole", "Whole display", [1.0], 0, "The entire display as one zone")];
        }

        var candidates = new List<SplitCandidate>();

        void Add(string id, string name, double[] weights, string rationale)
        {
            var normalized = Normalize(weights);
            candidates.Add(new SplitCandidate(
                id, name, normalized, Score(bounds, normalized, t), rationale));
        }

        Add("equal", $"Equal {Ordinal(count)}",
            [.. Enumerable.Repeat(1.0, count)],
            $"{count} equal columns");

        // Center-weighted, only meaningful for odd counts >= 3.
        if (count >= 3 && count % 2 == 1)
        {
            var w = Enumerable.Repeat(1.0, count).ToArray();
            w[count / 2] = 2.0;
            Add("center-weighted", "Wider center", w, "Center pane double width for a primary window");
        }

        // Content-anchored: size one pane to match a canonical ratio EXACTLY.
        // This is what lets 25/50/25 beat equal thirds on a 32:9 - a layout
        // containing a true 16:9 pane beats three merely-decent panes.
        foreach (var canonical in t.CanonicalRatios)
        {
            foreach (var anchored in AnchoredWeights(bounds, count, canonical.Ratio))
            {
                Add($"anchor-{canonical.Label}", $"Center {canonical.Label}", anchored,
                    $"Center pane is exactly {canonical.Label}");
            }
        }

        return [.. candidates
            .GroupBy(c => string.Join(',', c.Weights.Select(w => w.ToString("0.####"))))
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .OrderByDescending(c => c.Score)];
    }

    /// <summary>The highest-scoring split for a given count.</summary>
    public static SplitCandidate Best(PxRect bounds, int count, ShapeTuning? tuning = null) =>
        Candidates(bounds, count, tuning)[0];

    private static IEnumerable<double[]> AnchoredWeights(
        PxRect bounds, int count, double ratio)
    {
        // Only a symmetric odd split has a well-defined single center pane.
        if (count < 3 || count % 2 == 0) yield break;

        var longAxis = (double)bounds.LongAxis;
        var shortAxis = (double)bounds.ShortAxis;

        // Anchoring to the display's full short axis gives the clean fractions
        // (a 32:9 resolves to exactly 25/50/25). Anchoring to the work area
        // instead would give a mathematically exact pane but untidy numbers;
        // that variant is offered as a separate candidate by the caller.
        var centreLength = shortAxis * ratio;
        if (centreLength <= 0 || centreLength >= longAxis) yield break;

        var centreFraction = centreLength / longAxis;
        var sideFraction = (1.0 - centreFraction) / (count - 1);
        if (sideFraction <= 0) yield break;

        var weights = new double[count];
        for (var i = 0; i < count; i++) weights[i] = sideFraction;
        weights[count / 2] = centreFraction;

        yield return weights;
    }

    /// <summary>
    /// <code>
    /// score = mean(pane fit to preferred shape)
    ///       + ContentAnchorBonus * (display area covered by an EXACT canonical pane)
    ///       - mean(out-of-bounds penalty)
    /// </code>
    /// The anchor term is deliberately layout-level and area-weighted rather than
    /// a per-pane average: averaging lets three merely-decent panes outscore one
    /// perfect pane plus two side columns, which is backwards. The penalty term is
    /// what rejects a 16:9 center on a 21:9 - its side panes fall to aspect 0.31,
    /// far below the usable floor - with no threshold anywhere.
    /// </summary>
    private static double Score(PxRect bounds, IReadOnlyList<double> weights, ShapeTuning t)
    {
        var longAxis = (double)bounds.LongAxis;
        var shortAxis = (double)bounds.ShortAxis;

        var fitTotal = 0.0;
        var penaltyTotal = 0.0;
        var anchorArea = 0.0;

        foreach (var w in weights)
        {
            var aspect = longAxis * w / shortAxis;

            fitTotal += Fit(aspect, t.PreferredZoneAspect);

            // Area-weighted credit, only for a pane that is essentially exact.
            var bestAnchor = 0.0;
            foreach (var canonical in t.CanonicalRatios)
            {
                var fit = Fit(aspect, canonical.Ratio);
                if (fit >= t.ContentAnchorFitThreshold)
                    bestAnchor = Math.Max(bestAnchor, canonical.Weight);
            }

            anchorArea += w * bestAnchor;

            if (aspect < t.ZoneAspectMin || aspect > t.ZoneAspectMax)
            {
                var excess = aspect < t.ZoneAspectMin
                    ? t.ZoneAspectMin / Math.Max(aspect, 1e-6)
                    : aspect / t.ZoneAspectMax;
                penaltyTotal += 2.0 * excess;
            }
        }

        var count = weights.Count;
        return fitTotal / count + t.ContentAnchorBonus * anchorArea - penaltyTotal / count;
    }

    /// <summary>1.0 at an exact match, decaying with log-ratio distance.</summary>
    private static double Fit(double actual, double target)
    {
        if (actual <= 0 || target <= 0) return 0;
        var distance = Math.Abs(Math.Log(actual / target));
        return Math.Exp(-3.0 * distance);
    }

    private static IReadOnlyList<double> Normalize(IReadOnlyList<double> weights)
    {
        var sum = weights.Sum();
        return sum <= 0 ? weights : [.. weights.Select(w => w / sum)];
    }

    private static string Ordinal(int n) => n switch
    {
        2 => "halves",
        3 => "thirds",
        4 => "quarters",
        5 => "fifths",
        _ => $"{n} parts",
    };
}
