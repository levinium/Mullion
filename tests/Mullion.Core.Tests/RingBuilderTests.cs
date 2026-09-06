using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class RingBuilderTests
{
    private static readonly KeySurface Left = KeySurface.LeftHandBlock;

    [Fact]
    public void LeftColumnWidensLeftwardThenToFullWidth()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var layout = LayoutBuilder.Build(displays, Left);
        var work = displays[0].WorkArea;

        var zone = layout.At(Left.HomeRow, 0)!;   // Win+A, the left 25%
        var ring = RingBuilder.Build(zone, layout, displays);

        var rects = ring.Select(s => s.Parts[0].Area.Project(work)).ToList();

        rects[0].ShouldBe(new PxRect(0, 0, 1280, 1392));    // the zone itself
        rects[1].ShouldBe(new PxRect(0, 0, 2560, 1392));    // left half
        rects[2].ShouldBe(new PxRect(0, 0, 5120, 1392));    // whole display
    }

    [Fact]
    public void RightColumnWidensRightwardNotLeftward()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var layout = LayoutBuilder.Build(displays, Left);
        var work = displays[0].WorkArea;

        var zone = layout.At(Left.HomeRow, 2)!;   // Win+D, the right 25%
        var rects = RingBuilder.Build(zone, layout, displays)
            .Select(s => s.Parts[0].Area.Project(work)).ToList();

        rects[0].ShouldBe(new PxRect(3840, 0, 1280, 1392));
        rects[1].ShouldBe(new PxRect(2560, 0, 2560, 1392), "should widen toward its own edge");
    }

    /// <summary>
    /// A key bound to the upper half must widen within the upper half. Jumping
    /// to full height would be a different kind of action, not a wider version
    /// of the same one.
    /// </summary>
    [Fact]
    public void TierZonesStayWithinTheirBand()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var layout = LayoutBuilder.Build(displays, Left);
        var work = displays[0].WorkArea;

        var upper = layout.At(0, 0)!;   // Win+Q, upper-left
        var rects = RingBuilder.Build(upper, layout, displays)
            .Select(s => s.Parts[0].Area.Project(work)).ToList();

        rects.ShouldAllBe(r => r.Top == 0);
        rects.ShouldAllBe(r => r.Height <= 700);
    }

    [Fact]
    public void EveryStepIsDistinct()
    {
        foreach (var displays in new[]
                 {
                     TestDisplays.SuperUltrawideAlone(),
                     TestDisplays.ThreeAcross(),
                     TestDisplays.ThreePortraitAcross(),
                     TestDisplays.StandardPlusSuperUltrawide(),
                 })
        {
            var layout = LayoutBuilder.Build(displays, Left);

            foreach (var zone in layout.Zones)
            {
                var ring = RingBuilder.Build(zone, layout, displays);

                ring.Count.ShouldBeGreaterThan(0);
                ring.Select(s => s.Parts[0].Area).Distinct().Count().ShouldBe(ring.Count,
                    $"ring for {zone.Name} repeats a step");
            }
        }
    }

    /// <summary>
    /// With one display per column, the whole display is already on the home
    /// row, so the tier keys must not offer it again as a cycle step.
    /// </summary>
    [Fact]
    public void DoesNotOfferStepsAnotherKeyAlreadyReaches()
    {
        var displays = TestDisplays.ThreeAcross();
        var layout = LayoutBuilder.Build(displays, Left);

        var homeZone = layout.At(Left.HomeRow, 1)!;
        var ring = RingBuilder.Build(homeZone, layout, displays);

        // The home row already IS the whole display here, so there is nothing
        // wider to offer.
        ring.Count.ShouldBe(1);
    }

    [Fact]
    public void SpanningZonesGetASingleStep()
    {
        var displays = TestDisplays.VerticalsFlankingStackedPair();
        var layout = LayoutBuilder.Build(displays, Left);

        var union = layout.Zones.First(z => z.SpansDisplays);
        RingBuilder.Build(union, layout, displays).Count.ShouldBe(1);
    }
}
