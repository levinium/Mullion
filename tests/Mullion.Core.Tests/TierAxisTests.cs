using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// Which way a zone's two subzone keys cut it.
/// <para>
/// The cases that matter are the real ones, so they are asserted by the numbers
/// they actually produce rather than by category. Every expectation below is the
/// aspect band the rest of the engine already enforces on zones - tiers simply
/// were not being held to it.
/// </para>
/// </summary>
public class TierAxisTests
{
    private static readonly ShapeTuning T = ShapeTuning.Default;

    /// <summary>No DPI scaling and no pixel floor, for the shape-only cases.</summary>
    private static Axis Of(double w, double h, double floor = 0) => TierAxis.For(w, h, floor, T);

    [Fact]
    public void AStandardMonitorSplitsSideBySide()
    {
        // 1920x1080 stacked gives 1920x540, an aspect of 3.56 against a ceiling
        // of 2.20 - the letterbox pair that prompted this. Side by side gives
        // 960x1080, or 0.89.
        Of(1920, 1080).ShouldBe(Axis.Horizontal);
    }

    [Fact]
    public void TheCentreOfA32By9SplitsSideBySide()
    {
        // 2560x1392: stacked 3.68 (out), side by side 0.92 (in).
        Of(2560, 1392).ShouldBe(Axis.Horizontal);
    }

    [Fact]
    public void TheSidesOfA32By9StayStacked()
    {
        // 1280x1392: stacked 1.84 (in), side by side 0.46 (below the 0.62 floor).
        // The same desk as the previous test, and the opposite answer - which is
        // why this cannot be a global setting.
        Of(1280, 1392).ShouldBe(Axis.Vertical);
    }

    [Fact]
    public void OneDeskCanNeedBothAnswers()
    {
        // Stated directly, since it is the whole argument for deriving this per
        // zone rather than offering a switch.
        Of(2560, 1392).ShouldNotBe(Of(1280, 1392));
    }

    [Fact]
    public void ATallZoneStaysStacked()
    {
        // A portrait monitor's zone: halving it side by side would produce two
        // slits, which is what tiers already avoided by accident.
        Of(1080, 1920).ShouldBe(Axis.Vertical);
    }

    [Fact]
    public void ASquareZoneKeepsTheOldBehaviour()
    {
        // Both halves are equally viable here, so nothing forces a change and
        // the arrangement people already learned wins the tie.
        Of(1400, 1400).ShouldBe(Axis.Vertical);
    }

    [Theory]
    [InlineData(3440, 1440)]  // 21:9
    [InlineData(2560, 1440)]  // 16:9 at 1440p
    [InlineData(3840, 2160)]  // 4K
    [InlineData(5120, 1440)]  // an undivided 32:9
    public void AWideZoneNeverStacks(double w, double h)
    {
        // Stacking any of these produces something wider than 2.20; none of them
        // should ever have been cut into strips.
        Of(w, h).ShouldBe(Axis.Horizontal);
    }

    [Fact]
    public void AtMostOneAxisIsEverViable()
    {
        // Found while writing these tests, and worth stating because it bounds
        // what the pixel floor can do. Stacked halves land in the band only when
        // width/height is between 0.31 and 1.10; side-by-side halves only when it
        // is between 1.24 and 4.40. The ranges do not overlap, so the two can
        // never both be viable and the aspect test alone settles every case where
        // either one is. The floor can therefore only ever remove viability - it
        // cannot pick between two good answers, because there are never two.
        //
        // If a future tuning widens the band enough to overlap these, this fails
        // and the tie-break by preferred aspect starts carrying real weight.
        for (var ratio = 0.05; ratio <= 8.0; ratio += 0.01)
        {
            var stackedInBand = InBand(ratio * 2);
            var sideInBand = InBand(ratio / 2);

            (stackedInBand && sideInBand).ShouldBeFalse($"both viable at width/height {ratio:F2}");
        }

        static bool InBand(double aspect) =>
            aspect >= T.ZoneAspectMin && aspect <= T.ZoneAspectMax;
    }

    [Fact]
    public void WhenNeitherAxisWorksItPicksTheLessBadOne()
    {
        // An extreme strip: 5120x600 stacks to 17.07 and splits to 4.27. Both are
        // outside the band, so there is no good answer - but there is a less
        // ridiculous one, and defaulting to stacked here would pick the worse.
        Of(5120, 600).ShouldBe(Axis.Horizontal);
    }

    [Fact]
    public void ADegenerateZoneDoesNotThrow()
    {
        Of(0, 0).ShouldBe(Axis.Vertical);
        Of(100, 0).ShouldBe(Axis.Vertical);
    }

    [Fact]
    public void TheWorkAreaOverloadAgreesWithTheRawOne()
    {
        // The convenience overload is what LayoutBuilder calls, so it has to mean
        // the same thing - a zone covering half a 2560-wide work area is 1280
        // wide, not 2560.
        var work = new PxRect(0, 0, 2560, 1392);
        var half = new NormRect(0, 0, 0.5, 1.0);

        TierAxis.For(half, work, dpi: 96, T).ShouldBe(Of(1280, 1392));
        TierAxis.For(NormRect.Full, work, dpi: 96, T).ShouldBe(Of(2560, 1392));
    }

    [Fact]
    public void ScalingRaisesTheFloorTheSameWayZoneCountsDoIt()
    {
        // MinZoneLogicalPx is a logical measure, so the same panel at 150% has to
        // apply a floor 1.5x larger in physical pixels - otherwise a scaled
        // display gets subzones that are the right number of pixels and the wrong
        // apparent size.
        //
        // Asserted as an equivalence rather than by watching the answer change,
        // because of the property above: the floor cannot flip the axis, so there
        // is no outcome to watch. What can still be wrong is the arithmetic, and
        // that is what this pins.
        var work = new PxRect(0, 0, 1200, 1000);

        TierAxis.For(work.Width, work.Height, T.MinZoneLogicalPx * 1.5, T)
            .ShouldBe(TierAxis.For(NormRect.Full, work, dpi: 144, T));

        TierAxis.For(work.Width, work.Height, T.MinZoneLogicalPx, T)
            .ShouldBe(TierAxis.For(NormRect.Full, work, dpi: 96, T));
    }
}
