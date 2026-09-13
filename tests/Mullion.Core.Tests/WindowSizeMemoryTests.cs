using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class WindowSizeMemoryTests
{
    private const nint Window = 1234;

    private static readonly PxRect Chosen = new(300, 200, 900, 600);
    private static readonly PxRect Zone = new(1280, 0, 2560, 1392);
    private static readonly PxRect OtherZone = new(0, 0, 1280, 1392);

    [Fact]
    public void AWindowNeverSeenHasNothingToGoBackTo()
    {
        new WindowSizeMemory().ChosenSizeOf(Window).ShouldBeNull();
    }

    [Fact]
    public void TheFirstSizeSeenIsTheOnesOwnerChose()
    {
        var memory = new WindowSizeMemory();

        memory.Observe(Window, Chosen).ShouldBe(Chosen);
        memory.ChosenSizeOf(Window).ShouldBe(Chosen);
    }

    [Fact]
    public void FillingAZoneDoesNotBecomeTheRememberedSize()
    {
        // The whole point: after Mullion fills a zone, going "back" must mean
        // the size before that, not the zone.
        var memory = new WindowSizeMemory();

        memory.Observe(Window, Chosen);
        memory.Applied(Window, Zone);

        memory.ChosenSizeOf(Window).ShouldBe(Chosen);
    }

    [Fact]
    public void MovingBetweenZonesKeepsTheOriginalSize()
    {
        // Win+A then Win+S then Win+D: three moves, all ours, and the size to
        // go back to is still the one from before any of them.
        var memory = new WindowSizeMemory();

        memory.Observe(Window, Chosen);
        memory.Applied(Window, Zone);

        memory.Observe(Window, Zone);
        memory.Applied(Window, OtherZone);

        memory.Observe(Window, OtherZone);
        memory.Applied(Window, Zone);

        memory.ChosenSizeOf(Window).ShouldBe(Chosen);
    }

    [Fact]
    public void ResizingItYourselfReplacesWhatIsRemembered()
    {
        // Exactly the case asked for: fill a zone, drag the window smaller by
        // hand, fill a zone again - going back should mean the size just set by
        // hand, not the one from before all of it.
        var memory = new WindowSizeMemory();

        memory.Observe(Window, Chosen);
        memory.Applied(Window, Zone);

        var byHand = new PxRect(500, 400, 640, 480);

        memory.Observe(Window, byHand).ShouldBe(byHand);
        memory.ChosenSizeOf(Window).ShouldBe(byHand);
    }

    [Fact]
    public void AWindowNudgedAFewPixelsIsStillOurs()
    {
        // Windows that resist exact sizing land a pixel or two off, and that
        // must not read as the user having resized them.
        var memory = new WindowSizeMemory();

        memory.Observe(Window, Chosen);
        memory.Applied(Window, Zone);

        var landedShort = PxRect.FromLtrb(Zone.Left + 2, Zone.Top, Zone.Right - 3, Zone.Bottom - 1);

        memory.Observe(Window, landedShort).ShouldBe(Chosen);
        memory.ChosenSizeOf(Window).ShouldBe(Chosen);
    }

    [Fact]
    public void EachWindowIsRememberedSeparately()
    {
        var memory = new WindowSizeMemory();
        var other = (nint)5678;

        memory.Observe(Window, Chosen);
        memory.Observe(other, OtherZone);

        memory.ChosenSizeOf(Window).ShouldBe(Chosen);
        memory.ChosenSizeOf(other).ShouldBe(OtherZone);
    }

    [Fact]
    public void ItDoesNotGrowWithoutLimit()
    {
        // Handles are recycled, so a process running for weeks would otherwise
        // hold an entry for every window anything ever opened.
        var memory = new WindowSizeMemory();

        for (var i = 1; i <= 500; i++) memory.Observe(i, new PxRect(i, i, 100, 100));

        memory.Count.ShouldBeLessThanOrEqualTo(64);

        // The most recent survive; the oldest are the ones most likely gone.
        memory.ChosenSizeOf(500).ShouldNotBeNull();
        memory.ChosenSizeOf(1).ShouldBeNull();
    }

    [Fact]
    public void TheWholeToggleReadsCorrectlyFromEndToEnd()
    {
        // Fill, go back, fill again - the sequence the gesture promises.
        var memory = new WindowSizeMemory();

        memory.Observe(Window, Chosen);
        memory.Applied(Window, Zone);

        var back = ZoneFit.Restore(memory.ChosenSizeOf(Window)!.Value, Zone);
        memory.Observe(Window, Zone);
        memory.Applied(Window, back);

        memory.Observe(Window, back).ShouldBe(Chosen, "going back must not overwrite what we are going back to");

        memory.Applied(Window, Zone);
        memory.ChosenSizeOf(Window).ShouldBe(Chosen);
    }
    [Fact]
    public void DraggingAWindowIsNotChoosingANewSizeForIt()
    {
        // The gesture that carries the toggle is a drag, which moves a window
        // without resizing it. Counted as a fresh choice, the size remembered
        // would become the zone's own - and the toggle would then "restore" a
        // window to exactly the size it was already at.
        var memory = new WindowSizeMemory();

        memory.Observe(Window, Chosen);
        memory.Applied(Window, Zone);

        var dragged = new PxRect(Zone.Left + 340, Zone.Top + 210, Zone.Width, Zone.Height);
        memory.Observe(Window, dragged);

        memory.ChosenSizeOf(Window).ShouldBe(Chosen);
    }
}
