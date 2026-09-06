using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class LayoutEditorTests
{
    private static readonly KeySurface Left = KeySurface.LeftHandBlock;

    private static LayoutResult Layout() =>
        LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone(), Left);

    [Fact]
    public void MovingToAFreeKeyJustMoves()
    {
        var layout = Layout();
        var zone = layout.At(1, 0)!;   // Win+A

        // R is unbound on a three-column layout.
        var outcome = LayoutEditor.Rebind(layout, new GridPos(1, 0), new GridPos(1, 3));

        outcome.Success.ShouldBeTrue();
        outcome.Layout.At(1, 3)!.Name.ShouldBe(zone.Name);
        outcome.Layout.At(1, 0).ShouldBeNull();
        outcome.Message.ShouldContain("Win+F");
    }

    /// <summary>
    /// Overwriting would silently lose a zone the user still wants, with no clue
    /// where it went, so an occupied target swaps instead.
    /// </summary>
    [Fact]
    public void MovingToAnOccupiedKeySwaps()
    {
        var layout = Layout();
        var a = layout.At(1, 0)!;
        var d = layout.At(1, 2)!;

        var outcome = LayoutEditor.Rebind(layout, new GridPos(1, 0), new GridPos(1, 2));

        outcome.Success.ShouldBeTrue();
        outcome.Layout.At(1, 2)!.Name.ShouldBe(a.Name);
        outcome.Layout.At(1, 0)!.Name.ShouldBe(d.Name);
        outcome.Message.ShouldContain("Swapped");
    }

    [Fact]
    public void NoZoneIsEverLostByRebinding()
    {
        var layout = Layout();
        var before = layout.Zones.Select(z => z.Name).OrderBy(n => n).ToList();

        var outcome = LayoutEditor.Rebind(layout, new GridPos(1, 0), new GridPos(0, 2));

        outcome.Layout.Zones.Select(z => z.Name).OrderBy(n => n).ShouldBe(before);
        outcome.Layout.Zones.Select(z => z.Position).Distinct().Count()
            .ShouldBe(outcome.Layout.Zones.Count, "two zones must never share a key");
    }

    [Fact]
    public void RebindingToItselfIsANoOp()
    {
        var layout = Layout();

        var outcome = LayoutEditor.Rebind(layout, new GridPos(1, 1), new GridPos(1, 1));

        outcome.Success.ShouldBeTrue();
        outcome.Layout.ShouldBeSameAs(layout);
    }

    [Fact]
    public void RejectsAKeyOutsideTheSurface()
    {
        var layout = Layout();

        var outcome = LayoutEditor.Rebind(layout, new GridPos(1, 0), new GridPos(9, 9));

        outcome.Success.ShouldBeFalse();
        outcome.Message.ShouldContain("outside");
    }

    [Fact]
    public void RejectsAnEmptySource()
    {
        var layout = Layout();

        var outcome = LayoutEditor.Rebind(layout, new GridPos(1, 4), new GridPos(1, 3));

        outcome.Success.ShouldBeFalse();
    }

    [Fact]
    public void ResolvesScanCodesToGridPositions()
    {
        LayoutEditor.PositionOfScanCode(Left, 0x1E).ShouldBe(new GridPos(1, 0));   // A
        LayoutEditor.PositionOfScanCode(Left, 0x10).ShouldBe(new GridPos(0, 0));   // Q
        LayoutEditor.PositionOfScanCode(Left, 0x2E).ShouldBe(new GridPos(2, 2));   // C
        LayoutEditor.PositionOfScanCode(Left, 0x39).ShouldBeNull();                // space
    }

    /// <summary>
    /// The scan code for a physical key is the same whatever letter the active
    /// layout prints on it, so a rebind survives a keyboard-layout change.
    /// </summary>
    [Fact]
    public void ScanCodeLookupIsIndependentOfKeyboardLayout()
    {
        // 0x10 is the home-of-Q position: 'Q' on QWERTY, 'A' on AZERTY.
        LayoutEditor.PositionOfScanCode(Left, 0x10).ShouldBe(new GridPos(0, 0));
        LayoutEditor.PositionOfScanCode(KeySurface.Numpad, 0x4C).ShouldBe(new GridPos(1, 1));
    }
}
