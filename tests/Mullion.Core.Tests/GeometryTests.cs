using Mullion.Core.Geometry;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class NormRectTests
{
    /// <summary>
    /// The edge-rounding guarantee. Rounding position-and-size instead of edges
    /// leaves a 1px seam or overlap on most boundaries - visible on every snap.
    /// </summary>
    [Theory]
    [InlineData(5120, 1392, 3)]
    [InlineData(5120, 1392, 4)]
    [InlineData(3440, 1400, 3)]
    [InlineData(1920, 1032, 2)]
    [InlineData(2560, 1392, 5)]
    [InlineData(1366, 728, 3)]
    [InlineData(1023, 767, 7)]   // deliberately awkward, non-divisible
    public void AdjacentZonesShareExactEdges(int width, int height, int n)
    {
        var work = new PxRect(0, 0, width, height);
        var weights = Enumerable.Repeat(1.0, n).ToArray();

        var projected = NormRect.Full.Split(Axis.Horizontal, weights)
            .Select(z => z.Project(work))
            .ToList();

        projected[0].Left.ShouldBe(work.Left);
        projected[^1].Right.ShouldBe(work.Right);

        for (var i = 0; i < projected.Count - 1; i++)
        {
            projected[i].Right.ShouldBe(projected[i + 1].Left,
                $"seam between zone {i} and {i + 1} of {n} on {width}x{height}");
        }
    }

    /// <summary>Zones must tile the work area exactly: no gaps, no overlaps, no lost pixels.</summary>
    [Fact]
    public void SplitsTileTheWorkAreaExactlyAcrossManyGeometries()
    {
        int[] widths = [640, 1024, 1280, 1366, 1920, 2560, 3440, 5120, 7680];
        int[] heights = [480, 768, 1024, 1080, 1200, 1392, 1440, 2160];

        foreach (var w in widths)
        foreach (var h in heights)
        foreach (var n in new[] { 2, 3, 4, 5 })
        foreach (var axis in new[] { Axis.Horizontal, Axis.Vertical })
        {
            var work = new PxRect(-137, 42, w, h); // non-zero, negative origin
            var zones = NormRect.Full.Split(axis, Enumerable.Repeat(1.0, n).ToArray())
                .Select(z => z.Project(work))
                .ToList();

            zones.Sum(z => z.Area).ShouldBe(work.Area, $"{w}x{h} /{n} {axis} lost or gained pixels");
        }
    }

    [Fact]
    public void UnequalWeightsProduceTheExpectedFractions()
    {
        var work = new PxRect(0, 0, 5120, 1392);

        // The 25/50/25 that a 32:9 resolves to.
        var zones = NormRect.Full.Split(Axis.Horizontal, [0.25, 0.50, 0.25])
            .Select(z => z.Project(work))
            .ToList();

        zones[0].ShouldBe(new PxRect(0, 0, 1280, 1392));
        zones[1].ShouldBe(new PxRect(1280, 0, 2560, 1392));
        zones[2].ShouldBe(new PxRect(3840, 0, 1280, 1392));
    }

    [Fact]
    public void VerticalSplitTransposesCleanly()
    {
        var work = new PxRect(0, 0, 1440, 5072);

        var zones = NormRect.Full.Split(Axis.Vertical, [0.25, 0.50, 0.25])
            .Select(z => z.Project(work))
            .ToList();

        zones[0].ShouldBe(new PxRect(0, 0, 1440, 1268));
        zones[1].ShouldBe(new PxRect(0, 1268, 1440, 2536));
        zones[2].ShouldBe(new PxRect(0, 3804, 1440, 1268));
    }

    [Fact]
    public void UnionSpansFlankingZones()
    {
        var left = new NormRect(0, 0, 0.25, 1);
        var right = new NormRect(0.75, 0, 0.25, 1);

        left.Union(right).ShouldBe(NormRect.Full);
    }
}

public class PxRectTests
{
    [Fact]
    public void ElongationIsRotationInvariantButAspectIsNot()
    {
        var landscape = new PxRect(0, 0, 5120, 1440);
        var portrait = new PxRect(0, 0, 1440, 5120);

        landscape.Elongation.ShouldBe(portrait.Elongation, 1e-9);
        Math.Abs(landscape.Aspect - portrait.Aspect).ShouldBeGreaterThan(0.01);
    }

    [Fact]
    public void HandlesNegativeOriginsForDisplaysLeftOfPrimary()
    {
        var secondary = new PxRect(-2560, -120, 2560, 1440);

        secondary.Left.ShouldBe(-2560);
        secondary.Right.ShouldBe(0);
        secondary.Contains(-1, 0).ShouldBeTrue();
        secondary.Contains(0, 0).ShouldBeFalse();
    }

    [Fact]
    public void VerticalOverlapDetectsSameRowDisplays()
    {
        var a = new PxRect(0, 0, 2560, 1440);
        var b = new PxRect(2560, 360, 1920, 1080);   // bottom-aligned-ish, staggered
        var stacked = new PxRect(0, 1440, 2560, 1440);

        a.VerticalOverlap(b).ShouldBe(1080);
        a.VerticalOverlap(stacked).ShouldBe(0);
    }
}
