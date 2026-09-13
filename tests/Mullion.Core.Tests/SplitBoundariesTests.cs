using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class SplitBoundariesTests
{
    [Fact]
    public void ASingleSliceHasNoBoundaries()
    {
        // Its edges are the display's edges, and there is nothing on the far
        // side of those to trade with.
        SplitBoundaries.Of([1.0]).ShouldBeEmpty();
    }

    [Fact]
    public void BoundariesAreTheRunningTotals()
    {
        SplitBoundaries.Of([0.25, 0.5, 0.25])
            .ShouldBe([0.25, 0.75], tolerance: 1e-9);
    }

    [Fact]
    public void WeightsNeedNotSumToOne()
    {
        // They come straight from the split generator, which has no reason to
        // normalize them, and from config, which a person may have hand-edited.
        SplitBoundaries.Of([1.0, 2.0, 1.0])
            .ShouldBe([0.25, 0.75], tolerance: 1e-9);
    }

    [Fact]
    public void MovingASeamOnlyMovesThatSeam()
    {
        var moved = SplitBoundaries.Move([0.25, 0.5, 0.25], index: 0, position: 0.4, minFraction: 0.05);

        SplitBoundaries.Of(moved).ShouldBe([0.4, 0.75], tolerance: 1e-9);

        // The third slice is untouched: dragging the first seam must not shuffle
        // zones further down the display the user is not pointing at.
        moved[2].ShouldBe(0.25, 1e-9);
    }

    [Fact]
    public void MovingASeamConservesTheWhole()
    {
        var moved = SplitBoundaries.Move([0.25, 0.5, 0.25], index: 1, position: 0.6, minFraction: 0.05);

        moved.Sum().ShouldBe(1.0, 1e-9);
        moved[0].ShouldBe(0.25, 1e-9);
    }

    [Fact]
    public void ASeamStopsAtTheFloorRatherThanSnappingBack()
    {
        // Dragged well past what the neighbor can give up. Parking it at the
        // limit tracks the cursor; reverting to the original would read as the
        // handle being dropped.
        var moved = SplitBoundaries.Move([0.5, 0.5], index: 0, position: 0.01, minFraction: 0.1);

        SplitBoundaries.Of(moved).ShouldBe([0.1], tolerance: 1e-9);
    }

    [Fact]
    public void TheFloorNeverExceedsWhatThePairCanGive()
    {
        // Two slices sharing 10% of the display cannot both be 20% wide. The
        // floor yields rather than producing negative weights.
        var moved = SplitBoundaries.Move([0.45, 0.05, 0.05, 0.45], index: 1, position: 0.0, minFraction: 0.2);

        moved.ShouldAllBe(w => w > 0);
        moved.Sum().ShouldBe(1.0, 1e-9);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AnOutOfRangeSeamChangesNothing(int index)
    {
        // The last slice's far edge is the display edge, so index 2 of a
        // three-slice split names a seam that does not exist.
        double[] weights = [0.25, 0.5, 0.25];

        SplitBoundaries.Move(weights, index, 0.5, 0.05).ShouldBe(weights);
    }

    [Fact]
    public void RoundTripsThroughTheBoundaryItWasGiven()
    {
        // Legal for THIS seam means inside the span its own two slices share -
        // 0..0.75 here, less the floor at each end. A seam cannot be dragged
        // past its neighbor into a third slice.
        // The property the drag depends on: put a seam somewhere legal and it is
        // where you put it, so the handle lands under the cursor.
        foreach (var target in new[] { 0.06, 0.2, 0.35, 0.5, 0.69 })
        {
            var moved = SplitBoundaries.Move([0.25, 0.5, 0.25], index: 0, position: target, minFraction: 0.05);
            SplitBoundaries.Of(moved)[0].ShouldBe(target, 1e-9);
        }
    }
}
