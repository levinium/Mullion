using System.Runtime.Versioning;
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
    private static ZoneEditorViewModel Editor(string topologyId = "single-32-9")
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new WindowsAppHost(SimulatedTopologies.Find(topologyId));
        host.Start();

        return new ZoneEditorViewModel(host);
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

        editor.ToggleEditCommand.Execute(null);

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
        editor.ToggleEditCommand.Execute(null);

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
        editor.ToggleEditCommand.Execute(null);

        var before = editor.Diagram.Displays.Single().ZoneCount;

        editor.Diagram.Displays.Single().AddZoneCommand.Execute(null);
        editor.Diagram.Displays.Single().ZoneCount.ShouldBe(before + 1);

        editor.UndoCommand.Execute(null);
        editor.Diagram.Displays.Single().ZoneCount.ShouldBe(before);
    }

    [Fact]
    public void LeavingEditModeStopsListeningForAKey()
    {
        // A capture left armed takes the next key pressed anywhere, which is
        // about the worst thing a window can do on being dismissed.
        var editor = Editor();
        editor.ToggleEditCommand.Execute(null);

        editor.BeginRebindAt(new GridPos(1, 0));
        editor.Capturing.ShouldNotBeNull();

        editor.ToggleEditCommand.Execute(null);

        editor.Capturing.ShouldBeNull();
        editor.Message.ShouldBeNull();
    }

    [Fact]
    public void ClickingTheSameZoneTwiceCancelsTheCapture()
    {
        var editor = Editor();
        editor.ToggleEditCommand.Execute(null);

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
        editor.ToggleEditCommand.Execute(null);

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
        editor.ToggleEditCommand.Execute(null);

        var seen = new List<(GridPos? Was, GridPos? Now)>();
        editor.CapturingChanged += (was, now) => seen.Add((was, now));

        var cell = new GridPos(1, 0);
        editor.BeginRebindAt(cell);
        editor.CancelCapture();

        seen.ShouldBe([(null, cell), (cell, null)]);
    }
}
