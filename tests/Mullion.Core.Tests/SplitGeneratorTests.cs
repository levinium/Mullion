using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class SplitGeneratorTests
{
    private static PxRect R(int w, int h) => new(0, 0, w, h);

    /// <summary>
    /// The headline case. On a 32:9 the content-anchored split must beat equal
    /// thirds, producing 25/50/25 with a true 16:9 centre - and it must do so
    /// through scoring, with no super-ultrawide branch anywhere in the code.
    /// </summary>
    [Fact]
    public void SuperUltrawideResolvesTo25_50_25()
    {
        var best = SplitGenerator.Best(R(5120, 1440), 3);

        best.Weights[0].ShouldBe(0.25, 0.001);
        best.Weights[1].ShouldBe(0.50, 0.001);
        best.Weights[2].ShouldBe(0.25, 0.001);
    }

    /// <summary>
    /// The counter-case, which is what proves the rule generalises. On a 21:9 a
    /// 16:9 centre would need 74% of the width, leaving side panes far outside
    /// the aspect bounds. The penalty must reject it in favour of equal thirds -
    /// again with no threshold involved.
    /// </summary>
    [Fact]
    public void UltrawideRejectsThe16x9CentreInFavourOfEqualThirds()
    {
        var best = SplitGenerator.Best(R(3440, 1440), 3);

        best.Weights[0].ShouldBe(1.0 / 3, 0.01);
        best.Weights[1].ShouldBe(1.0 / 3, 0.01);
        best.Weights[2].ShouldBe(1.0 / 3, 0.01);
    }

    [Fact]
    public void ProjectsToTheExactPixelsForTheUsersMachine()
    {
        var work = new PxRect(0, 0, 5120, 1392);
        var best = SplitGenerator.Best(R(5120, 1440), 3);

        var zones = NormRect.Full.Split(Axis.Horizontal, best.Weights)
            .Select(z => z.Project(work))
            .ToList();

        zones[0].ShouldBe(new PxRect(0, 0, 1280, 1392));
        zones[1].ShouldBe(new PxRect(1280, 0, 2560, 1392));
        zones[2].ShouldBe(new PxRect(3840, 0, 1280, 1392));
    }

    /// <summary>A 32:9 turned on its side must resolve to the same proportions, stacked.</summary>
    [Fact]
    public void RotatedSuperUltrawideResolvesToTheSameProportions()
    {
        var landscape = SplitGenerator.Best(R(5120, 1440), 3);
        var portrait = SplitGenerator.Best(R(1440, 5120), 3);

        for (var i = 0; i < 3; i++)
            portrait.Weights[i].ShouldBe(landscape.Weights[i], 0.001);
    }

    [Fact]
    public void EveryCandidateTilesExactly()
    {
        foreach (var (w, h) in new[] { (5120, 1440), (3440, 1440), (2560, 1440), (7680, 1440) })
        foreach (var n in new[] { 2, 3, 4, 5 })
        {
            foreach (var candidate in SplitGenerator.Candidates(R(w, h), n))
            {
                candidate.Weights.Sum().ShouldBe(1.0, 1e-9,
                    $"{w}x{h} /{n} candidate '{candidate.Id}' weights must sum to 1");
                candidate.Weights.Count.ShouldBe(n);
                candidate.Weights.ShouldAllBe(x => x > 0);
            }
        }
    }

    [Fact]
    public void CandidatesAreRankedBestFirst()
    {
        var candidates = SplitGenerator.Candidates(R(5120, 1440), 3);

        candidates.Count.ShouldBeGreaterThan(1);
        for (var i = 0; i < candidates.Count - 1; i++)
            candidates[i].Score.ShouldBeGreaterThanOrEqualTo(candidates[i + 1].Score);
    }

    [Fact]
    public void SingleCountReturnsTheWholeDisplay()
    {
        var best = SplitGenerator.Best(R(2560, 1440), 1);

        best.Weights.Count.ShouldBe(1);
        best.Weights[0].ShouldBe(1.0);
    }
}
