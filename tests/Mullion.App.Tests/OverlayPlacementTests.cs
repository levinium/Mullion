using Mullion.App.Services;
using Mullion.Core.Geometry;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Sizing an overlay window over a rectangle measured in physical pixels.
/// <para>
/// The two units meeting here are the whole problem. Avalonia positions a window
/// in physical pixels and sizes it in device-independent ones, and Mullion's
/// zones are physical from end to end - so passing the same numbers to both drew
/// every overlay at its monitor's scale factor. Nobody noticed because the desk
/// it was written on runs at 100%, where the two are the same number.
/// </para>
/// </summary>
public class OverlayPlacementTests
{
    private static readonly PxRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void AtOneHundredPercentTheNumbersAreUnchanged()
    {
        // The case that hid this for so long, and the one that must not move.
        var size = OverlayPlacement.SizeFor(Monitor, 1.0);

        size.Width.ShouldBe(1920);
        size.Height.ShouldBe(1080);
    }

    [Fact]
    public void AScaledMonitorGetsAProportionallySmallerBox()
    {
        // 1920 physical pixels are 1280 device-independent ones at 150%. Passing
        // 1920 straight through asked for 2880 physical - the overlay that ran
        // most of the way across the next monitor.
        var size = OverlayPlacement.SizeFor(Monitor, 1.5);

        size.Width.ShouldBe(1280);
        size.Height.ShouldBe(720);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void TheBoxAlwaysComesBackToTheRectangleItCame(double scaling)
    {
        // The property that matters, stated directly: whatever the scale, the
        // physical rectangle the window ends up covering is the one asked for.
        var size = OverlayPlacement.SizeFor(Monitor, scaling);

        (size.Width * scaling).ShouldBe(Monitor.Width, 1e-9);
        (size.Height * scaling).ShouldBe(Monitor.Height, 1e-9);
    }

    [Fact]
    public void AZoneRatherThanAWholeMonitorScalesTheSameWay()
    {
        // Zones are what this actually draws; a third of a 4K display at 150%.
        var size = OverlayPlacement.SizeFor(new PxRect(1280, 0, 1280, 1392), 1.5);

        size.Width.ShouldBe(1280 / 1.5, 1e-9);
        size.Height.ShouldBe(1392 / 1.5, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void AnImpossibleScaleIsTreatedAsUnscaled(double scaling)
    {
        // Rather than dividing by it. A screen that reports nonsense should cost
        // an overlay of the wrong size at worst, not one of infinite size or none.
        var size = OverlayPlacement.SizeFor(Monitor, scaling);

        size.Width.ShouldBe(1920);
        size.Height.ShouldBe(1080);
    }

    [Fact]
    public void AnEmptyRectangleStillHasSomethingToDraw()
    {
        // A zero-sized overlay is not a smaller mistake than an oversized one, it
        // is an invisible one - and it would read as the flash never firing.
        var size = OverlayPlacement.SizeFor(new PxRect(100, 100, 0, 0), 1.0);

        size.Width.ShouldBeGreaterThan(0);
        size.Height.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void NoScreensAtAllIsNotAFailure()
    {
        // Screens can be unavailable early in startup. Unscaled is the right
        // guess and the only one that cannot throw.
        OverlayPlacement.ScaleOf(null, Monitor).ShouldBe(1.0);
    }
}
