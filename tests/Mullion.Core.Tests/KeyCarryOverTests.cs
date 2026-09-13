using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// Whether a key someone chose survives the layout being rebuilt.
/// <para>
/// It did not. The layout is regenerated from scratch on every zone count
/// change, every dragged seam, every undo, every key-surface change and every
/// display change, and the builder that does it is given displays, a surface
/// and zone shapes - never the chord a zone was bound to. So a rebind lasted
/// exactly until the next thing you did, and did so silently.
/// </para>
/// </summary>
public class KeyCarryOverTests
{
    private const ChordModifiers CtrlShift = ChordModifiers.Control | ChordModifiers.Shift;

    private static LayoutResult Fresh() =>
        LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());

    /// <summary>A layout with one zone bound to something of its own.</summary>
    private static (LayoutResult Layout, GridPos Where) Rebound()
    {
        var layout = Fresh();
        var where = layout.Zones[0].Position;

        var outcome = LayoutEditor.Rebind(layout, where, where, CtrlShift);
        outcome.Success.ShouldBeTrue();

        return (outcome.Layout, where);
    }

    [Fact]
    public void AChosenModifierSurvivesARebuild()
    {
        var (previous, where) = Rebound();

        var carried = LayoutEditor.CarryKeysOver(Fresh(), previous);

        carried.At(where)!.Modifier.ShouldBe(CtrlShift);
    }

    [Fact]
    public void ARebuildWithNothingBeforeItIsLeftAlone()
    {
        var fresh = Fresh();

        LayoutEditor.CarryKeysOver(fresh, null).ShouldBeSameAs(fresh);
    }

    [Fact]
    public void ZonesNobodyTouchedKeepTheirDerivedKeys()
    {
        // Carrying over must not mean overriding: a zone left alone still
        // follows the allocator, and the default modifier setting still reaches
        // it.
        var (previous, where) = Rebound();

        var carried = LayoutEditor.CarryKeysOver(Fresh(), previous);

        carried.Zones
            .Where(z => z.Position != where)
            .ShouldAllBe(z => z.Modifier == null);
    }

    [Fact]
    public void ASubzoneKeepsItsChordWhenASeamFlipsItsAxis()
    {
        // Subzones are cut whichever way leaves two usable windows, so widening a
        // column can change its halves from stacked to side by side - and with
        // them their names, from "Center upper" to "Center left". Identity is the
        // name, because a name is the one thing a resize used not to touch. It
        // does now, and without allowing for it a dragged seam silently drops the
        // chord off both halves of every zone whose axis flipped.
        var layout = Fresh();

        // The LEFT column's first half - the one that actually flips. On the
        // default 25/50/25 the left column is 1280x1392, which stacks into
        // 1280x696 halves; widened to 40% it becomes 2048x1392, which splits into
        // 1024x1392 halves instead. So the zone is renamed "Left upper" to "Left
        // left" by nothing but a dragged seam.
        //
        // The CENTRE is the wrong zone to test with, and quietly so: it is wide
        // enough to split side by side at both widths, so the name never changes
        // and the assertion passes with or without the fix.
        var tier = layout.Zones.Single(z => z.Position == new GridPos(0, 0));
        tier.Name.ShouldEndWith("upper");

        var bound = LayoutEditor.Rebind(layout, tier.Position, tier.Position, CtrlShift);
        bound.Success.ShouldBeTrue();

        var widened = LayoutBuilder.Build(
            TestDisplays.SuperUltrawideAlone(),
            overrides: [new DisplayOverride("5120x1440@0,0", 3, [0.4, 0.3, 0.3])]);

        widened.Zones.Single(z => z.Position == new GridPos(0, 0))
            .Name.ShouldEndWith("left");

        var carried = LayoutEditor.CarryKeysOver(widened, bound.Layout);

        carried.Zones.Single(z => z.Position == new GridPos(0, 0))
            .Modifier.ShouldBe(CtrlShift, "the chord must outlive the rename the resize caused");
    }

    [Fact]
    public void AKeyMovedToAnotherZoneSurvivesASeamDrag()
    {
        // The case the shape-based comparison cannot serve. Dragging a seam
        // changes every shape it touches, so a zone has to be recognised by
        // something a resize does not alter.
        var layout = Fresh();
        var from = layout.Zones[0].Position;
        var to = layout.Zones[1].Position;

        var swapped = LayoutEditor.Rebind(layout, from, to).Layout;

        var resized = LayoutBuilder.Build(
            TestDisplays.SuperUltrawideAlone(),
            overrides: [new DisplayOverride("5120x1440@0,0", 3, [0.4, 0.3, 0.3])]);

        var carried = LayoutEditor.CarryKeysOver(resized, swapped);

        LayoutEditor.SameKeyAssignments(carried, resized)
            .ShouldBeFalse("the swap should have come across the resize");
    }

    [Fact]
    public void ChangingTheZoneCountDoesNotPutTwoZonesOnOneKey()
    {
        // The old assignment was a bijection over a set of zones that is gone.
        // Carrying it wholesale would put two zones on one key; the modifiers
        // that still have an owner come across, and the keys are re-derived.
        var (previous, _) = Rebound();

        var fewer = LayoutBuilder.Build(
            TestDisplays.SuperUltrawideAlone(),
            overrides: [new DisplayOverride("5120x1440@0,0", 2, null)]);

        var carried = LayoutEditor.CarryKeysOver(fewer, previous);

        carried.Zones
            .Select(z => z.Position)
            .Distinct()
            .Count()
            .ShouldBe(carried.Zones.Count, "two zones ended up on the same key");
    }

    [Fact]
    public void ADeskThatChangedCompletelyCarriesNothing()
    {
        var (previous, _) = Rebound();

        var elsewhere = LayoutBuilder.Build(TestDisplays.ThreeAcross());

        var carried = LayoutEditor.CarryKeysOver(elsewhere, previous);

        carried.Zones.ShouldAllBe(z => z.Modifier == null);
    }
    [Fact]
    public void UndoThenRedoWalksTheKeyBackAndForward()
    {
        // The whole round trip as the app performs it: record the rebind, undo
        // it, redo it. Undo worked and redo did not, which is the sort of thing
        // only the full sequence catches.
        var before = Fresh();
        var history = new LayoutHistory(new LayoutSnapshot([], before));

        var (rebound, where) = Rebound();
        history.Record(new LayoutSnapshot([], rebound));

        var undone = history.Undo();
        undone.ShouldNotBeNull();

        LayoutEditor.CarryKeysOver(Fresh(), undone!.Layout)
            .At(where)!.Modifier.ShouldBeNull("undo should put the default back");

        var redone = history.Redo();
        redone.ShouldNotBeNull();

        LayoutEditor.CarryKeysOver(Fresh(), redone!.Layout)
            .At(where)!.Modifier.ShouldBe(CtrlShift, "redo should bring the chord back");
    }
}
