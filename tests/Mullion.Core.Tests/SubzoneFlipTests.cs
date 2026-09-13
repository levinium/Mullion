using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// The other orientation of a subzone, which is what its key answers with when
/// Shift is held.
/// <para>
/// Derived from rectangles rather than from grid positions, and these assertions
/// are mostly about why: a subzone can be rebound to any key on the surface, and
/// a layout restored from a saved profile arrives as zones and nothing else. Any
/// rule phrased as "the key above home" breaks on the first rebind and on every
/// restart.
/// </para>
/// </summary>
public class SubzoneFlipTests
{
    private static readonly PxRect Work = TestDisplays.SuperUltrawideAlone()[0].WorkArea;

    private static LayoutResult Fresh() =>
        LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());

    private static PxRect? FlipOf(LayoutResult layout, GridPos at)
    {
        var parts = SubzoneFlip.Of(layout.At(at)!, layout);
        return parts is null ? null : parts[0].Area.Project(Work);
    }

    [Fact]
    public void AStackedHalfFlipsToASideBySideOne()
    {
        // The left column is 1280x1392, stacked into 1280x696 halves. Its first
        // half held with Shift gives the left 640x1392 instead.
        FlipOf(Fresh(), new GridPos(0, 0)).ShouldBe(new PxRect(0, 0, 640, 1392));
    }

    [Fact]
    public void TheSecondHalfFlipsToTheSecondOfTheOtherAxis()
    {
        // Lower becomes right, not left: the slot is kept, only the axis changes.
        FlipOf(Fresh(), new GridPos(2, 0)).ShouldBe(new PxRect(640, 0, 640, 1392));
    }

    [Fact]
    public void ASideBySideHalfFlipsToAStackedOne()
    {
        // The centre is 2560x1392, split into 1280x1392 halves. Its first half
        // flips to the upper 2560x696.
        FlipOf(Fresh(), new GridPos(0, 1)).ShouldBe(new PxRect(1280, 0, 2560, 696));

        FlipOf(Fresh(), new GridPos(2, 1)).ShouldBe(new PxRect(1280, 696, 2560, 696));
    }

    [Fact]
    public void AWholeZoneHasNoOtherOrientation()
    {
        // It is not a half of anything, so Shift has nothing to offer and the key
        // must stay unbound rather than answer with something invented.
        SubzoneFlip.Of(Fresh().At(new GridPos(1, 1))!, Fresh()).ShouldBeNull();
    }

    [Fact]
    public void TheFlipIsTheSameSizeAsTheHalfItReplaces()
    {
        // Both are half the parent, so whatever else changes, the area does not.
        var layout = Fresh();

        foreach (var zone in layout.Zones)
        {
            var flipped = SubzoneFlip.Of(zone, layout);
            if (flipped is null) continue;

            var was = zone.Parts[0].Area;
            var now = flipped[0].Area;

            (now.W * now.H).ShouldBe(was.W * was.H, 1e-9);
        }
    }

    [Fact]
    public void FlippingTwiceComesBackToWhereItStarted()
    {
        // The clearest statement that this is one axis toggle and not a walk
        // through arrangements: Shift is "the other one", both ways.
        var layout = Fresh();
        var zone = layout.At(new GridPos(0, 1))!;

        var once = SubzoneFlip.Of(zone, layout)!;
        var twice = SubzoneFlip.Of(zone with { Parts = once }, layout)!;

        twice[0].Area.Project(Work).ShouldBe(zone.Parts[0].Area.Project(Work));
    }

    [Fact]
    public void ARebindDoesNotChangeWhatShiftGives()
    {
        // The reason this reads rectangles and not positions. Moving a subzone
        // onto a different key moves nothing on the desk, so its other
        // orientation is the same rectangle it was before.
        var layout = Fresh();
        var before = FlipOf(layout, new GridPos(0, 0));

        var moved = LayoutEditor.Rebind(layout, new GridPos(0, 0), new GridPos(0, 4)).Layout;

        FlipOf(moved, new GridPos(0, 4)).ShouldBe(before);
    }

    [Fact]
    public void AHandPinnedAxisIsFlippedFromWhereItActuallyIs()
    {
        // Shift means "the other one" relative to what the zone IS, not relative
        // to what the shape rule would have chosen. Pinning the centre to stacked
        // must therefore make Shift give side by side.
        var pinned = LayoutBuilder.Build(
            TestDisplays.SuperUltrawideAlone(),
            overrides: [new DisplayOverride("5120x1440@0,0", SubzoneAxes: [null, DisplayOverride.Stacked, null])]);

        pinned.At(new GridPos(0, 1))!.Parts[0].Area.Project(Work)
            .ShouldBe(new PxRect(1280, 0, 2560, 696));

        FlipOf(pinned, new GridPos(0, 1)).ShouldBe(new PxRect(1280, 0, 1280, 1392));
    }
}
