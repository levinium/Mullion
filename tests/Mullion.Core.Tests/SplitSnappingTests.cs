using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class SplitSnappingTests
{
    // The machine this was built for: a 5120x1440 with a 48px taskbar.
    private static readonly PxRect Ultrawide = new(0, 0, 5120, 1392);

    /// <summary>A pane of exactly 16:9 on that display, as a fraction of its width.</summary>
    private const double ExactPane = 1392 * (16.0 / 9.0) / 5120;

    private static IReadOnlyList<double> Across(double from = 0, double to = 1) =>
        SplitSnapping.Candidates(from, to, Ultrawide.Width, Ultrawide.Height);

    [Fact]
    public void TheGridIsOffered()
    {
        var candidates = Across();

        candidates.ShouldContain(c => Math.Abs(c - 0.25) < 1e-9);
        candidates.ShouldContain(c => Math.Abs(c - 0.50) < 1e-9);
        candidates.ShouldContain(c => Math.Abs(c - 0.95) < 1e-9);
    }

    [Fact]
    public void TheDisplaysOwnEdgesAreNotCandidates()
    {
        // A seam at 0 or 1 is a zone of no width, which the floor forbids anyway.
        Across().ShouldAllBe(c => c > 0 && c < 1);
    }

    [Fact]
    public void AnExactSixteenByNinePaneIsReachable()
    {
        Across().ShouldContain(c => Math.Abs(c - ExactPane) < 1e-9);

        // And it is genuinely off-grid, or this test proves nothing at all.
        Math.Abs(ExactPane - Math.Round(ExactPane / 0.05) * 0.05).ShouldBeGreaterThan(0.005);
    }

    [Fact]
    public void AnExactPaneIsOfferedFromEitherEnd()
    {
        var candidates = Across();

        candidates.ShouldContain(c => Math.Abs(c - ExactPane) < 1e-9);
        candidates.ShouldContain(c => Math.Abs(c - (1 - ExactPane)) < 1e-9);
    }

    [Fact]
    public void ThirdsAreOfferedBecauseNoUsefulGridHasThem()
    {
        Across().ShouldContain(c => Math.Abs(c - 1.0 / 3.0) < 1e-9);
        Across().ShouldContain(c => Math.Abs(c - 2.0 / 3.0) < 1e-9);
    }

    [Fact]
    public void CandidatesAreMeasuredFromThePairBeingResized()
    {
        // The seam between zones two and three of a 25/50/25 moves within
        // 0.25..1.0, so an exact pane there starts at 0.25 and not at zero.
        var candidates = SplitSnapping.Candidates(0.25, 1.0, Ultrawide.Width, Ultrawide.Height);

        candidates.ShouldContain(c => Math.Abs(c - (0.25 + ExactPane)) < 1e-9);
        candidates.ShouldAllBe(c => c > 0.25 && c < 1.0);
    }

    [Fact]
    public void SnappingTakesTheNearest()
    {
        SplitSnapping.Snap(0.26, [0.25, 0.5, 0.75]).ShouldBe(0.25);
        SplitSnapping.Snap(0.49, [0.25, 0.5, 0.75]).ShouldBe(0.5);
    }

    [Fact]
    public void WithNothingToSnapToThePositionIsLeftAlone()
    {
        SplitSnapping.Snap(0.371, []).ShouldBe(0.371);
    }

    [Fact]
    public void ADraggedSeamIsNeverFarFromACandidate()
    {
        // What makes snapping feel helpful rather than restrictive: wherever the
        // cursor is, something acceptable is close by. Stated from half a step
        // in from each edge - nearer than that there is nothing outside to snap
        // to, and the zone floor stops a seam getting there anyway.
        var candidates = Across();
        var edge = SplitSnapping.DefaultStep / 2;

        for (var at = edge; at < 1 - edge; at += 0.001)
            Math.Abs(SplitSnapping.Snap(at, candidates) - at)
                .ShouldBeLessThanOrEqualTo(SplitSnapping.DefaultStep / 2 + 1e-9);
    }

    [Fact]
    public void APortraitDisplayMeasuresAcrossItsWidth()
    {
        // Rotated, the axis a pane aspect is measured against swaps over.
        var portrait = new PxRect(0, 0, 1440, 5120);

        SplitSnapping.Extents(portrait, horizontal: false).ShouldBe((5120, 1440));
        SplitSnapping.Extents(portrait, horizontal: true).ShouldBe((1440, 5120));
    }

    [Fact]
    public void AStepOfZeroLeavesOnlyTheMeaningfulPositions()
    {
        // With the grid off the exact-aspect positions should still be there:
        // they are the ones that are hard to hit by hand.
        var candidates = SplitSnapping.Candidates(0, 1, 5120, 1392, step: 0);

        candidates.ShouldNotBeEmpty();
        candidates.ShouldContain(c => Math.Abs(c - ExactPane) < 1e-9);
    }
}
