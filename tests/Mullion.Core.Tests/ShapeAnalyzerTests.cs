using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class ShapeAnalyzerTests
{
    private static PxRect R(int w, int h) => new(0, 0, w, h);

    // The classification table from the plan. These are the hand-reasoned
    // decisions the continuous formula has to reproduce without any of them
    // being written down anywhere in the implementation.
    [Theory]
    // display,       dpi, others, expMin, expMax, expPreferred
    [InlineData(2560, 1440, 96, true, 1, 2, 1)]   // 16:9 beside others -> whole monitor
    [InlineData(2560, 1440, 96, false, 1, 2, 2)]  // 16:9 alone -> halves
    [InlineData(1920, 1080, 96, true, 1, 2, 1)]
    [InlineData(3440, 1440, 96, false, 2, 3, 2)]  // 21:9 -> two
    [InlineData(3440, 1440, 96, true, 2, 3, 2)]   // ...even alongside others
    [InlineData(5120, 1440, 96, false, 2, 5, 3)]  // 32:9 -> three (the user's machine)
    [InlineData(5120, 1440, 96, true, 2, 5, 3)]   // ...and still three in multi-monitor
    // 48:9. Max is 8 because that is what the GEOMETRY supports (960x1440 panes,
    // aspect 0.67, inside the bounds). The key surface caps this to 5 columns
    // later, in the allocator - that is a budget concern, not a shape concern.
    [InlineData(7680, 1440, 96, false, 3, 8, 5)]
    [InlineData(2256, 1504, 96, false, 1, 2, 2)]  // 3:2 laptop - never in any table
    [InlineData(1280, 1024, 96, false, 1, 2, 2)]  // 5:4 legacy - never in any table
    [InlineData(1080, 1920, 96, false, 1, 2, 2)]  // portrait 16:9 -> stacked halves
    [InlineData(1440, 5120, 96, false, 2, 5, 3)]  // 32:9 ROTATED -> three stacked
    public void ProducesTheDocumentedRanges(
        int w, int h, uint dpi, bool others, int expMin, int expMax, int expPreferred)
    {
        var range = ShapeAnalyzer.ZoneCounts(R(w, h), dpi, others);

        range.Min.ShouldBe(expMin, $"{w}x{h} min");
        range.Max.ShouldBe(expMax, $"{w}x{h} max");
        range.Preferred.ShouldBe(expPreferred, $"{w}x{h} preferred");
    }

    /// <summary>
    /// The property that motivated dropping the enum. A rotated 32:9 is still a
    /// 32:9; classifying it as a generic "portrait" display would throw that away
    /// and give it a tall-monitor layout instead of three stacked zones.
    /// </summary>
    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(3440, 1440)]
    [InlineData(5120, 1440)]
    [InlineData(7680, 1440)]
    [InlineData(2256, 1504)]
    [InlineData(1280, 1024)]
    [InlineData(3840, 2160)]
    public void IsInvariantUnderRotation(int w, int h)
    {
        var landscape = ShapeAnalyzer.ZoneCounts(R(w, h), 96, hasOtherDisplays: false);
        var portrait = ShapeAnalyzer.ZoneCounts(R(h, w), 96, hasOtherDisplays: false);

        portrait.ShouldBe(landscape);
    }

    [Theory]
    [InlineData(2560, 1440, Orientation.Landscape, Axis.Horizontal)]
    [InlineData(1440, 2560, Orientation.Portrait, Axis.Vertical)]
    [InlineData(5120, 1440, Orientation.Landscape, Axis.Horizontal)]
    [InlineData(1440, 5120, Orientation.Portrait, Axis.Vertical)]
    public void OrientationPicksTheSplitAxis(int w, int h, Orientation expected, Axis axis)
    {
        ShapeAnalyzer.OrientationOf(R(w, h)).ShouldBe(expected);
        ShapeAnalyzer.SplitAxis(R(w, h)).ShouldBe(axis);
    }

    /// <summary>
    /// No cliff edges. This is the test that would have caught the arbitrary
    /// 2.1 / 2.9 / 4.5 thresholds the discarded enum was hiding.
    /// </summary>
    [Fact]
    public void RangesAreMonotonicAcrossTheElongationSweep()
    {
        const int height = 1440;
        var previous = ShapeAnalyzer.ZoneCounts(R(height, height), 96, false);

        for (var r = 1.0; r <= 6.0; r += 0.01)
        {
            var range = ShapeAnalyzer.ZoneCounts(R((int)Math.Round(height * r), height), 96, false);

            range.Min.ShouldBeGreaterThanOrEqualTo(previous.Min, $"min regressed at R={r:0.00}");
            range.Max.ShouldBeGreaterThanOrEqualTo(previous.Max, $"max regressed at R={r:0.00}");
            range.Min.ShouldBeLessThanOrEqualTo(range.Max, $"empty range at R={r:0.00}");
            range.Allows(range.Preferred).ShouldBeTrue($"preferred outside range at R={r:0.00}");

            previous = range;
        }
    }

    /// <summary>Every count the analyzer permits must actually produce a usable zone.</summary>
    [Fact]
    public void EveryPermittedCountYieldsAZoneInsideTheBounds()
    {
        var t = ShapeTuning.Default;

        for (var r = 1.0; r <= 6.0; r += 0.05)
        {
            var bounds = R((int)Math.Round(1440 * r), 1440);
            var range = ShapeAnalyzer.ZoneCounts(bounds, 96, false);

            for (var n = range.Min; n <= range.Max; n++)
            {
                var aspect = ShapeAnalyzer.ZoneAspectAt(bounds, n);
                aspect.ShouldBeGreaterThan(t.ZoneAspectMin - 0.02, $"R={r:0.00} n={n} too narrow");
                aspect.ShouldBeLessThan(t.ZoneAspectMax + 0.02, $"R={r:0.00} n={n} too wide");
            }
        }
    }

    /// <summary>
    /// The pixel floor catches what aspect ratio alone cannot: the same panel at
    /// 150% scaling has fewer usable zones because each would be too small.
    /// </summary>
    [Fact]
    public void PixelFloorScalesWithDpi()
    {
        // Must be a display where the PIXEL floor binds before the aspect bound,
        // otherwise DPI cannot influence the answer and the test proves nothing.
        // On 5120x1440 the aspect bound allows 5 but 200% scaling cuts it to 4.
        var bounds = R(5120, 1440);

        var at100 = ShapeAnalyzer.ZoneCounts(bounds, 96, false);
        var at200 = ShapeAnalyzer.ZoneCounts(bounds, 192, false);

        at100.Max.ShouldBe(5);
        at200.Max.ShouldBe(4);
    }

    [Fact]
    public void NovelAspectRatiosStillProduceUsableLayouts()
    {
        // 12:5, 1:1, 5:1 - none of which appear in any table in the design.
        foreach (var (w, h) in new[] { (2880, 1200), (1440, 1440), (7200, 1440) })
        {
            var range = ShapeAnalyzer.ZoneCounts(R(w, h), 96, false);

            range.Min.ShouldBeGreaterThanOrEqualTo(1);
            range.Max.ShouldBeGreaterThanOrEqualTo(range.Min);
            range.Allows(range.Preferred).ShouldBeTrue();
        }
    }
}
