using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class ZoneFitTests
{
    private static readonly PxRect Zone = new(1280, 0, 2560, 1392);

    [Fact]
    public void AWindowSittingExactlyInAZoneFillsIt()
    {
        ZoneFit.Fills(Zone, Zone).ShouldBeTrue();
    }

    [Fact]
    public void AWindowAFewPixelsShortStillFillsIt()
    {
        // Terminals snap to whole character cells and some apps have minimums,
        // so a window that was told to fill a zone often lands slightly inside
        // it. The toggle must not stop working because of three pixels.
        var shy = PxRect.FromLtrb(
            Zone.Left + 3, Zone.Top + 2, Zone.Right - 4, Zone.Bottom - 1);

        ZoneFit.Fills(shy, Zone).ShouldBeTrue();
    }

    [Fact]
    public void AWindowThatMerelyOverlapsDoesNotFillIt()
    {
        var half = PxRect.FromLtrb(Zone.Left, Zone.Top, Zone.Left + Zone.Width / 2, Zone.Bottom);

        ZoneFit.Fills(half, Zone).ShouldBeFalse();
    }

    [Fact]
    public void AWindowInADifferentZoneDoesNotFillThisOne()
    {
        var elsewhere = new PxRect(0, 0, 1280, 1392);

        ZoneFit.Fills(elsewhere, Zone).ShouldBeFalse();
    }

    [Fact]
    public void ARestoredWindowKeepsItsOwnSize()
    {
        var remembered = new PxRect(400, 300, 900, 600);

        var restored = ZoneFit.Restore(remembered, Zone);

        restored.Width.ShouldBe(900);
        restored.Height.ShouldBe(600);
    }

    [Fact]
    public void ARestoredWindowLandsInTheZoneItWasDroppedIn()
    {
        // Centred rather than returned to where it came from: the window is
        // being dropped HERE, so here is where it stays.
        var remembered = new PxRect(0, 0, 900, 600);

        var restored = ZoneFit.Restore(remembered, Zone);

        restored.Left.ShouldBeGreaterThanOrEqualTo(Zone.Left);
        restored.Right.ShouldBeLessThanOrEqualTo(Zone.Right);

        Math.Abs((restored.Left + restored.Right) - (Zone.Left + Zone.Right)).ShouldBeLessThanOrEqualTo(1);
        Math.Abs((restored.Top + restored.Bottom) - (Zone.Top + Zone.Bottom)).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void AWindowLargerThanTheZoneIsCappedToIt()
    {
        // Otherwise restoring throws it straight back out of the zone it was
        // just dropped into.
        var huge = new PxRect(0, 0, 5000, 3000);

        var restored = ZoneFit.Restore(huge, Zone);

        restored.Width.ShouldBe(Zone.Width);
        restored.Height.ShouldBe(Zone.Height);
        restored.ShouldBe(Zone);
    }

    [Fact]
    public void RestoringThenFillingComesBackToTheSameZone()
    {
        // The round trip the gesture promises: out, back, and out again.
        var original = new PxRect(700, 400, 800, 500);

        var restored = ZoneFit.Restore(original, Zone);

        ZoneFit.Fills(restored, Zone).ShouldBeFalse("it should not still count as filling the zone");
        ZoneFit.Fills(Zone, Zone).ShouldBeTrue();
    }
}
