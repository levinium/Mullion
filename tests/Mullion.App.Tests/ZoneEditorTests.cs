using System.Runtime.Versioning;
using Mullion.App.Controls;
using Mullion.App.Services;
using Mullion.App.ViewModels;
using Mullion.Core.Hotkeys;
using Mullion.Core.Simulation;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// The editor shared by the main window and settings.
/// <para>
/// Its whole point is that the two windows behave the same, so what is asserted
/// here is the behaviour neither window should have to restate: read-only until
/// asked, editable after, and nothing left listening when it is dismissed.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class ZoneEditorTests
{
    /// <summary>Zone counts per display: what an edit here visibly changes.</summary>
    private static IReadOnlyList<int> Shape(ZoneEditorViewModel editor) =>
        [.. editor.Diagram.Displays.Select(d => d.ZoneCount)];

    private static ZoneEditorViewModel Editor(string topologyId = "single-32-9")
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new WindowsAppHost(SimulatedTopologies.Find(topologyId));
        host.Start();

        return new ZoneEditorViewModel(host);
    }


    [Fact]
    public void CancellingPutsEverythingBack()
    {
        // The whole point of the cross: what was changed during the session is
        // undone in one go, however many changes there were.
        var editor = Editor();
        var before = Shape(editor);

        editor.BeginEditCommand.Execute(null);

        var display = editor.Diagram.Displays.Single();
        display.AddZoneCommand.Execute(null);
        editor.Diagram.Displays.Single().AddZoneCommand.Execute(null);

        Shape(editor).ShouldNotBe(before);

        editor.CancelEditCommand.Execute(null);

        Shape(editor).ShouldBe(before);
        editor.IsEditing.ShouldBeFalse();
    }

    [Fact]
    public void ConfirmingKeepsTheChanges()
    {
        var editor = Editor();
        var before = Shape(editor);

        editor.BeginEditCommand.Execute(null);
        editor.Diagram.Displays.Single().AddZoneCommand.Execute(null);

        var edited = Shape(editor);
        editor.ConfirmEditCommand.Execute(null);

        Shape(editor).ShouldBe(edited);
        Shape(editor).ShouldNotBe(before);
    }

    [Fact]
    public void CancellingTakesTheUndoHistoryWithIt()
    {
        // Undo after a cancel would walk back into edits that were just thrown
        // away, which is the one place it must not go.
        var editor = Editor();

        editor.BeginEditCommand.Execute(null);
        editor.Diagram.Displays.Single().AddZoneCommand.Execute(null);
        editor.CanUndo.ShouldBeTrue();

        editor.CancelEditCommand.Execute(null);
        editor.BeginEditCommand.Execute(null);

        editor.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void CancellingASessionThatChangedNothingIsHarmless()
    {
        var editor = Editor();
        var before = Shape(editor);

        editor.BeginEditCommand.Execute(null);
        editor.CancelEditCommand.Execute(null);

        Shape(editor).ShouldBe(before);
    }

    [Fact]
    public void ADraggedSplitIsAlsoReverted()
    {
        // Weights come through a different path from the steppers, and are the
        // edit most easily made by accident.
        var editor = Editor();

        editor.BeginEditCommand.Execute(null);

        var display = editor.Diagram.Displays.Single();
        var before = display.Cells.OrderBy(c => c.Area.X).First().Area.Width;

        display.Handles[0].Dragged!(new SeamDrag(0, 0.4, true));
        display.Handles[0].Released!();

        editor.Diagram.Displays.Single().Cells.OrderBy(c => c.Area.X).First()
            .Area.Width.ShouldNotBe(before);

        editor.CancelEditCommand.Execute(null);

        editor.Diagram.Displays.Single().Cells.OrderBy(c => c.Area.X).First()
            .Area.Width.ShouldBe(before, 1e-6);
    }

    [Fact]
    public void TheButtonsSwapWhenTheSessionOpensAndCloses()
    {
        var editor = Editor();

        editor.ShowEditButton.ShouldBeTrue();
        editor.ShowSessionButtons.ShouldBeFalse();

        editor.BeginEditCommand.Execute(null);

        editor.ShowEditButton.ShouldBeFalse();
        editor.ShowSessionButtons.ShouldBeTrue();

        editor.ConfirmEditCommand.Execute(null);

        editor.ShowEditButton.ShouldBeTrue();
        editor.ShowSessionButtons.ShouldBeFalse();
    }

    [Fact]
    public void SettingsHasNoSessionSoItsChangesStandOnTheirOwn()
    {
        // No pencil there, so nothing could ever confirm or cancel: a
        // provisional state with no way to resolve it is just changes that
        // never get saved.
        var editor = Editor();
        editor.CanEdit = false;
        editor.IsEditing = true;

        editor.ShowEditButton.ShouldBeFalse();
        editor.ShowSessionButtons.ShouldBeFalse();
    }
    [Fact]
    public void ItStartsAsAPictureRatherThanAControl()
    {
        // The main window is somewhere people glance at. A diagram that is
        // always live turns a stray click on a zone into a rebind prompt.
        var editor = Editor();

        editor.IsEditing.ShouldBeFalse();
        editor.Diagram.Displays.ShouldAllBe(d => !d.HasHandles);
        editor.Diagram.Displays.ShouldAllBe(d => !d.CanEditZoneCount);
        editor.Diagram.Displays.SelectMany(d => d.Cells).ShouldAllBe(c => !c.IsInteractive);
    }

    [Fact]
    public void EditingTurnsOnEveryWayOfChangingIt()
    {
        var editor = Editor();

        editor.BeginEditCommand.Execute(null);

        editor.IsEditing.ShouldBeTrue();
        editor.Diagram.Displays.ShouldAllBe(d => d.HasHandles);
        editor.Diagram.Displays.ShouldAllBe(d => d.CanEditZoneCount);
        editor.Diagram.Displays.SelectMany(d => d.Cells).ShouldAllBe(c => c.IsInteractive);
    }

    [Fact]
    public void TheControlsAreOffWhileItIsAPicture()
    {
        // Undo that is pressable on a read-only diagram would change a layout
        // the window is not offering to change.
        var editor = Editor();

        editor.CanUndo.ShouldBeFalse();
        editor.CanRedo.ShouldBeFalse();
        editor.CanResetZones.ShouldBeFalse();
    }

    [Fact]
    public void UndoBecomesAvailableOnceThereIsSomethingToUndo()
    {
        var editor = Editor();
        editor.BeginEditCommand.Execute(null);

        editor.CanUndo.ShouldBeFalse();

        var display = editor.Diagram.Displays.Single();
        display.AddZoneCommand.Execute(null);

        editor.CanUndo.ShouldBeTrue();
        editor.UndoCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void AZoneCountStepFromTheDiagramIsUndoable()
    {
        var editor = Editor();
        editor.BeginEditCommand.Execute(null);

        var before = editor.Diagram.Displays.Single().ZoneCount;

        editor.Diagram.Displays.Single().AddZoneCommand.Execute(null);
        editor.Diagram.Displays.Single().ZoneCount.ShouldBe(before + 1);

        editor.UndoCommand.Execute(null);
        editor.Diagram.Displays.Single().ZoneCount.ShouldBe(before);
    }

    [Fact]
    public void EndingTheSessionStopsListeningForAKey()
    {
        // A capture left armed takes the next key pressed anywhere, which is
        // about the worst thing a window can do on being dismissed.
        var editor = Editor();
        editor.BeginEditCommand.Execute(null);

        editor.BeginRebindAt(new GridPos(1, 0));
        editor.Capturing.ShouldNotBeNull();

        editor.ConfirmEditCommand.Execute(null);

        editor.Capturing.ShouldBeNull();
        editor.Message.ShouldBeNull();
    }

    [Fact]
    public void ClickingTheSameZoneTwiceCancelsTheCapture()
    {
        var editor = Editor();
        editor.BeginEditCommand.Execute(null);

        var cell = new GridPos(1, 0);

        editor.BeginRebindAt(cell);
        editor.BeginRebindAt(cell);

        editor.Capturing.ShouldBeNull();
    }

    [Fact]
    public void ACaptureSurvivesTheDiagramBeingRebuilt()
    {
        // Every edit replaces the diagram, so a zone waiting for a key would
        // quietly stop looking like it.
        var editor = Editor();
        editor.BeginEditCommand.Execute(null);

        var cell = new GridPos(1, 0);
        editor.BeginRebindAt(cell);

        editor.Refresh();

        editor.Diagram.Displays.SelectMany(d => d.Cells)
            .Count(c => c.IsCapturing)
            .ShouldBe(1);
    }

    [Fact]
    public void TheCapturedCellIsReportedForTheListToFollow()
    {
        var editor = Editor();
        editor.BeginEditCommand.Execute(null);

        var seen = new List<(GridPos? Was, GridPos? Now)>();
        editor.CapturingChanged += (was, now) => seen.Add((was, now));

        var cell = new GridPos(1, 0);
        editor.BeginRebindAt(cell);
        editor.CancelCapture();

        seen.ShouldBe([(null, cell), (cell, null)]);
    }
}
