using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class LayoutHistoryTests
{
    private static DisplayOverride Split(int columns, params double[] weights) =>
        new("5120x1440@0,0", columns, weights.Length == 0 ? null : weights);

    /// <summary>
    /// A state made only of shapes. The history now carries the keys as well,
    /// which these tests are not about - a null layout means "no opinion", so
    /// they go on saying exactly what they said.
    /// </summary>
    private static LayoutSnapshot Shapes(params DisplayOverride[] overrides) =>
        new(overrides, null);

    [Fact]
    public void NothingToUndoAtTheStart()
    {
        var history = new LayoutHistory();

        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
        history.Undo().ShouldBeNull();
    }

    [Fact]
    public void UndoGoesBackToWhatWasThereBefore()
    {
        var history = new LayoutHistory(Shapes(Split(3, 0.25, 0.5, 0.25)));

        history.Record(Shapes(Split(3, 0.4, 0.35, 0.25)));

        history.CanUndo.ShouldBeTrue();
        history.Undo()!.Overrides.Single().Weights.ShouldBe([0.25, 0.5, 0.25]);
    }

    [Fact]
    public void RedoComesForwardAgain()
    {
        var history = new LayoutHistory(Shapes(Split(3, 0.25, 0.5, 0.25)));
        history.Record(Shapes(Split(3, 0.4, 0.35, 0.25)));

        history.Undo();
        history.CanRedo.ShouldBeTrue();

        history.Redo()!.Overrides.Single().Weights.ShouldBe([0.4, 0.35, 0.25]);
        history.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void SeveralEditsUndoInOrder()
    {
        var history = new LayoutHistory(Shapes(Split(2)));

        history.Record(Shapes(Split(3)));
        history.Record(Shapes(Split(4)));

        history.Undo()!.Overrides.Single().Columns.ShouldBe(3);
        history.Undo()!.Overrides.Single().Columns.ShouldBe(2);
        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void AnEditAfterUndoingAbandonsWhatWasUndone()
    {
        // Otherwise redo jumps to a state that no longer follows from this one.
        var history = new LayoutHistory(Shapes(Split(2)));

        history.Record(Shapes(Split(3)));
        history.Undo();
        history.Record(Shapes(Split(5)));

        history.CanRedo.ShouldBeFalse();
        history.Present.Overrides.Single().Columns.ShouldBe(5);
    }

    [Fact]
    public void AnEditThatChangesNothingCostsNoUndo()
    {
        // Releasing a seam reports even when the pointer never moved, and a
        // click that did nothing should not need an undo press to get past.
        var history = new LayoutHistory(Shapes(Split(3, 0.25, 0.5, 0.25)));

        history.Record(Shapes(Split(3, 0.25, 0.5, 0.25)));

        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void OrderOfOverridesIsNotAnEdit()
    {
        // The set is keyed by slot, so two lists holding the same overrides in
        // a different order are the same state.
        var a = new DisplayOverride("A", 2);
        var b = new DisplayOverride("B", 3);

        var history = new LayoutHistory(Shapes(a, b));
        history.Record(Shapes(b, a));

        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void ClearingEveryOverrideIsItselfUndoable()
    {
        // "Reset zones" is an edit like any other: done by accident, it has to
        // be recoverable.
        var history = new LayoutHistory(Shapes(Split(3, 0.4, 0.35, 0.25)));

        history.Record(Shapes());

        history.Present.Overrides.ShouldBeEmpty();
        history.Undo()!.Overrides.Single().Weights.ShouldBe([0.4, 0.35, 0.25]);
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        // A display change: the states that were undoable describe monitors that
        // may no longer be attached.
        var history = new LayoutHistory(Shapes(Split(2)));
        history.Record(Shapes(Split(3)));

        history.Reset(Shapes(Split(4)));

        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
        history.Present.Overrides.Single().Columns.ShouldBe(4);
    }

    [Fact]
    public void TheHistoryIsBounded()
    {
        var history = new LayoutHistory(Shapes(Split(1)));

        for (var i = 2; i < 200; i++) history.Record(Shapes(Split(i)));

        var depth = 0;
        while (history.Undo() is not null) depth++;

        depth.ShouldBeLessThanOrEqualTo(64);
        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void ARecordedStateIsCopied()
    {
        // Held by reference, a caller mutating its own list afterwards would
        // silently rewrite history.
        var mutable = new List<DisplayOverride> { Split(3) };

        var history = new LayoutHistory(Shapes(Split(2)));
        history.Record(new LayoutSnapshot(mutable, null));

        mutable.Clear();

        history.Present.Overrides.Count.ShouldBe(1);
    }
    // ---- Keys, not just shapes ---------------------------------------------
    //
    // A rebind changes the layout and leaves the override set exactly as it
    // was. Comparing override sets alone therefore read every rebind as
    // "nothing happened", so nothing was recorded, Undo stayed grey, and the
    // one edit you could not take back was the one most likely to be a mistake.

    private static LayoutResult Layout() =>
        LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());

    private static LayoutResult Rebound(LayoutResult from) =>
        LayoutEditor.Rebind(
            from,
            from.Zones[0].Position,
            from.Zones[0].Position,
            Mullion.Core.Hotkeys.ChordModifiers.Control | Mullion.Core.Hotkeys.ChordModifiers.Shift).Layout;

    [Fact]
    public void ARebindIsAnEditEvenThoughNoShapeChanged()
    {
        var before = Layout();
        var history = new LayoutHistory(new LayoutSnapshot([], before));

        history.Record(new LayoutSnapshot([], Rebound(before)));

        history.CanUndo.ShouldBeTrue("a rebind is an edit and has to be undoable");
    }

    [Fact]
    public void UndoingARebindPutsTheOldKeyBack()
    {
        var before = Layout();
        var history = new LayoutHistory(new LayoutSnapshot([], before));

        history.Record(new LayoutSnapshot([], Rebound(before)));

        var undone = history.Undo();

        undone.ShouldNotBeNull();
        undone!.Layout!.At(before.Zones[0].Position)!.Modifier
            .ShouldBeNull("the zone should be back on the default modifier");
    }

    [Fact]
    public void RecordingTheSameKeysTwiceIsStillNotAnEdit()
    {
        // The guard that stops a click doing nothing from costing an undo press
        // has to survive the history learning about keys.
        var layout = Layout();
        var history = new LayoutHistory(new LayoutSnapshot([], layout));

        history.Record(new LayoutSnapshot([], Layout()));

        history.CanUndo.ShouldBeFalse();
    }
    [Fact]
    public void AStateWithNoLayoutIsNotTheSameAsOneWithKeysOnIt()
    {
        // The exact shape of the bug the fix above was written for. The history
        // is seeded before the first layout exists, so its state has no keys;
        // reading "no layout" as "the same keys" made the first rebind after
        // every launch compare equal to the seed and vanish.
        var history = new LayoutHistory(new LayoutSnapshot([], null));

        history.Record(new LayoutSnapshot([], Layout()));

        history.CanUndo.ShouldBeTrue("learning the keys is a change from not knowing them");
    }
}
