using Mullion.Core.Hotkeys;
using Mullion.Core.Simulation;
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

    [Fact]
    public void AFreshlyBuiltLayoutMatchesItself()
    {
        // What the grayed-out "reset keys" button turns on: nothing has moved,
        // so there is nothing to put back.
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;

        var a = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var b = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);

        LayoutEditor.SameKeyAssignments(a, b).ShouldBeTrue();
    }

    [Fact]
    public void MovingAKeyIsNoticed()
    {
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;

        var original = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var home = original.Surface.HomeRow;

        var moved = LayoutEditor.Rebind(
            original, new GridPos(home, 0), new GridPos(home, 4));

        moved.Success.ShouldBeTrue();
        LayoutEditor.SameKeyAssignments(original, moved.Layout).ShouldBeFalse();
    }

    [Fact]
    public void MovingAKeyBackIsNoticedToo()
    {
        // The half that a flag gets wrong: it is set when a key moves and then
        // has to be cleared again when the layout comes back to where it began.
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;

        var original = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var home = original.Surface.HomeRow;

        var moved = LayoutEditor.Rebind(original, new GridPos(home, 0), new GridPos(home, 4));
        var back = LayoutEditor.Rebind(moved.Layout, new GridPos(home, 4), new GridPos(home, 0));

        LayoutEditor.SameKeyAssignments(original, back.Layout).ShouldBeTrue();
    }

    [Fact]
    public void ZonesAreMatchedByShapeNotByIdentity()
    {
        // Regenerating gives every zone a fresh Guid, so comparing identities
        // would report every layout as customized and leave the reset button
        // permanently lit.
        var displays = SimulatedTopologies.Find("three-across")!.Displays;

        var a = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var b = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);

        a.Zones.Select(z => z.Id).ShouldNotBe(b.Zones.Select(z => z.Id));
        LayoutEditor.SameKeyAssignments(a, b).ShouldBeTrue();
    }

    [Fact]
    public void AZoneCanBeBoundWithItsOwnModifier()
    {
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;
        var layout = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var home = layout.Surface.HomeRow;

        var outcome = LayoutEditor.Rebind(
            layout, new GridPos(home, 0), new GridPos(home, 0),
            ChordModifiers.Control | ChordModifiers.Alt, ChordModifiers.Win);

        outcome.Success.ShouldBeTrue();

        var zone = outcome.Layout.Zones.First(z => z.Position == new GridPos(home, 0));
        zone.Modifier.ShouldBe(ChordModifiers.Control | ChordModifiers.Alt);
    }

    [Fact]
    public void TheSameKeyWithADifferentModifierIsNotACollision()
    {
        // Win+A and Ctrl+Alt+A are different hotkeys. Treating the key alone as
        // the identity would swap two zones that never conflicted.
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;
        var layout = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var home = layout.Surface.HomeRow;

        var target = new GridPos(home, 1);
        var occupant = layout.Zones.First(z => z.Position == target).Name;

        var outcome = LayoutEditor.Rebind(
            layout, new GridPos(home, 0), target,
            ChordModifiers.Control | ChordModifiers.Alt, ChordModifiers.Win);

        outcome.Message.ShouldNotContain("Swapped");

        // The zone that already held that key keeps it, on its own chord.
        outcome.Layout.Zones
            .Count(z => z.Position == target)
            .ShouldBe(2, "both zones sit on the key, with different modifiers");

        outcome.Layout.Zones
            .First(z => z.Name == occupant)
            .Position.ShouldBe(target);
    }

    [Fact]
    public void TheSameChordStillSwaps()
    {
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;
        var layout = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var home = layout.Surface.HomeRow;

        var outcome = LayoutEditor.Rebind(
            layout, new GridPos(home, 0), new GridPos(home, 1), null, ChordModifiers.Win);

        outcome.Message.ShouldContain("Swapped");
    }

    [Fact]
    public void AZoneLeftOnTheDefaultFollowsIt()
    {
        // The whole point of it being a DEFAULT: a zone nobody has bound by hand
        // has no modifier of its own, so changing the setting moves it.
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;
        var layout = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);

        layout.Zones.ShouldAllBe(z => z.Modifier == null);

        layout.Zones.ShouldAllBe(z => z.ChordWith(ChordModifiers.Alt) == ChordModifiers.Alt);
    }

    [Fact]
    public void AZoneBoundByHandKeepsItsChordWhenTheDefaultChanges()
    {
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;
        var layout = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var home = layout.Surface.HomeRow;

        var bound = LayoutEditor.Rebind(
            layout, new GridPos(home, 0), new GridPos(home, 0),
            ChordModifiers.Control | ChordModifiers.Alt, ChordModifiers.Win).Layout;

        var zone = bound.Zones.First(z => z.Modifier is not null);

        zone.ChordWith(ChordModifiers.Alt)
            .ShouldBe(ChordModifiers.Control | ChordModifiers.Alt,
                "binding it by hand was the act of choosing");
    }

    [Fact]
    public void ChangingOnlyTheModifierCountsAsACustomKey()
    {
        var displays = SimulatedTopologies.Find("single-32-9")!.Displays;
        var layout = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var home = layout.Surface.HomeRow;

        var bound = LayoutEditor.Rebind(
            layout, new GridPos(home, 0), new GridPos(home, 0),
            ChordModifiers.Control | ChordModifiers.Alt, ChordModifiers.Win).Layout;

        LayoutEditor.SameKeyAssignments(layout, bound).ShouldBeFalse();
    }
}
