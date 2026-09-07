using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class LayoutHistoryTests
{
    private static DisplayOverride Split(int columns, params double[] weights) =>
        new("5120x1440@0,0", columns, weights.Length == 0 ? null : weights);

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
        var history = new LayoutHistory([Split(3, 0.25, 0.5, 0.25)]);

        history.Record([Split(3, 0.4, 0.35, 0.25)]);

        history.CanUndo.ShouldBeTrue();
        history.Undo()!.Single().Weights.ShouldBe([0.25, 0.5, 0.25]);
    }

    [Fact]
    public void RedoComesForwardAgain()
    {
        var history = new LayoutHistory([Split(3, 0.25, 0.5, 0.25)]);
        history.Record([Split(3, 0.4, 0.35, 0.25)]);

        history.Undo();
        history.CanRedo.ShouldBeTrue();

        history.Redo()!.Single().Weights.ShouldBe([0.4, 0.35, 0.25]);
        history.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void SeveralEditsUndoInOrder()
    {
        var history = new LayoutHistory([Split(2)]);

        history.Record([Split(3)]);
        history.Record([Split(4)]);

        history.Undo()!.Single().Columns.ShouldBe(3);
        history.Undo()!.Single().Columns.ShouldBe(2);
        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void AnEditAfterUndoingAbandonsWhatWasUndone()
    {
        // Otherwise redo jumps to a state that no longer follows from this one.
        var history = new LayoutHistory([Split(2)]);

        history.Record([Split(3)]);
        history.Undo();
        history.Record([Split(5)]);

        history.CanRedo.ShouldBeFalse();
        history.Present.Single().Columns.ShouldBe(5);
    }

    [Fact]
    public void AnEditThatChangesNothingCostsNoUndo()
    {
        // Releasing a seam reports even when the pointer never moved, and a
        // click that did nothing should not need an undo press to get past.
        var history = new LayoutHistory([Split(3, 0.25, 0.5, 0.25)]);

        history.Record([Split(3, 0.25, 0.5, 0.25)]);

        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void OrderOfOverridesIsNotAnEdit()
    {
        // The set is keyed by slot, so two lists holding the same overrides in
        // a different order are the same state.
        var a = new DisplayOverride("A", 2);
        var b = new DisplayOverride("B", 3);

        var history = new LayoutHistory([a, b]);
        history.Record([b, a]);

        history.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void ClearingEveryOverrideIsItselfUndoable()
    {
        // "Reset zones" is an edit like any other: done by accident, it has to
        // be recoverable.
        var history = new LayoutHistory([Split(3, 0.4, 0.35, 0.25)]);

        history.Record([]);

        history.Present.ShouldBeEmpty();
        history.Undo()!.Single().Weights.ShouldBe([0.4, 0.35, 0.25]);
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        // A display change: the states that were undoable describe monitors that
        // may no longer be attached.
        var history = new LayoutHistory([Split(2)]);
        history.Record([Split(3)]);

        history.Reset([Split(4)]);

        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
        history.Present.Single().Columns.ShouldBe(4);
    }

    [Fact]
    public void TheHistoryIsBounded()
    {
        var history = new LayoutHistory([Split(1)]);

        for (var i = 2; i < 200; i++) history.Record([Split(i)]);

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

        var history = new LayoutHistory([Split(2)]);
        history.Record(mutable);

        mutable.Clear();

        history.Present.Count.ShouldBe(1);
    }
}
