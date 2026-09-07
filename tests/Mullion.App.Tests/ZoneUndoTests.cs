using System.Runtime.Versioning;
using Mullion.App.Services;
using Mullion.Core.Layout;
using Mullion.Core.Simulation;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Undo and redo driven through the real host, not a fake.
/// <para>
/// The history itself is covered in Core. What is not covered anywhere else is
/// the wiring: whether an edit is recorded, whether undoing puts the overrides
/// back, and whether the layout is rebuilt from them afterwards. Twice today a
/// piece of wiring exactly this size was wrong while everything either side of
/// it was right, so it gets a test of its own.
/// </para>
/// <para>
/// Run against a simulated arrangement, so config saving is disabled and the
/// machine's real settings are only ever read.
/// </para>
/// </summary>
/// <remarks>Windows only: the host is where the platform actually lives.</remarks>
[SupportedOSPlatform("windows")]
public class ZoneUndoTests
{
    private static WindowsAppHost Host(string topologyId)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new WindowsAppHost(SimulatedTopologies.Find(topologyId));
        host.Start();
        return host;
    }

    /// <summary>
    /// Zone counts per display, which is what an edit here visibly changes.
    /// Compared rather than the overrides themselves so the assertions are about
    /// the layout the user sees, not the bookkeeping behind it.
    /// </summary>
    private static IReadOnlyList<int> Shape(WindowsAppHost host) =>
        [.. host.GetCustomizations().Select(c => c.Columns)];

    [Fact]
    public void ThereIsNothingToUndoBeforeAnyEdit()
    {
        using var host = Host("single-32-9");

        host.CanUndoZones.ShouldBeFalse();
        host.CanRedoZones.ShouldBeFalse();
    }

    [Fact]
    public void UndoPutsAZoneCountBack()
    {
        using var host = Host("single-32-9");

        var before = Shape(host);
        var slot = host.GetCustomizations()[0].Slot;

        host.SetDisplayColumns(slot, before[0] == 2 ? 3 : 2);
        Shape(host).ShouldNotBe(before);

        host.CanUndoZones.ShouldBeTrue();
        host.UndoZones();

        Shape(host).ShouldBe(before);
    }

    [Fact]
    public void RedoPutsItBackAgain()
    {
        using var host = Host("single-32-9");

        var slot = host.GetCustomizations()[0].Slot;
        var before = Shape(host);

        host.SetDisplayColumns(slot, before[0] == 2 ? 3 : 2);
        var edited = Shape(host);

        host.UndoZones();
        host.CanRedoZones.ShouldBeTrue();
        host.RedoZones();

        Shape(host).ShouldBe(edited);
    }

    [Fact]
    public void UndoWalksBackThroughSeveralEdits()
    {
        using var host = Host("single-32-9");

        var slot = host.GetCustomizations()[0].Slot;
        var before = Shape(host);

        host.SetDisplayColumns(slot, 2);
        host.SetDisplayColumns(slot, 4);
        host.SetDisplayColumns(slot, 5);

        host.UndoZones();
        Shape(host)[0].ShouldBe(4);

        host.UndoZones();
        Shape(host)[0].ShouldBe(2);

        host.UndoZones();
        Shape(host).ShouldBe(before);
        host.CanUndoZones.ShouldBeFalse();
    }

    [Fact]
    public void ADraggedSplitIsUndoable()
    {
        // The edit the user actually makes most often, and the one that reports
        // through a different path from the steppers.
        using var host = Host("single-32-9");

        var slot = host.GetCustomizations()[0].Slot;
        var before = host.GetCustomizations()[0].Weights;

        host.SetDisplayWeights(slot, [0.4, 0.35, 0.25]);
        host.GetCustomizations()[0].Weights.ShouldNotBe(before);

        host.UndoZones();

        host.GetCustomizations()[0].Weights
            .Zip(before, (a, b) => Math.Abs(a - b))
            .ShouldAllBe(d => d < 1e-6);
    }

    [Fact]
    public void ResettingEveryZoneIsItselfUndoable()
    {
        // Pressed by accident it wipes a layout that took effort to build, so it
        // has to be recoverable like any other edit.
        using var host = Host("single-32-9");

        var slot = host.GetCustomizations()[0].Slot;

        host.SetDisplayColumns(slot, 5);
        var edited = Shape(host);

        host.ResetAllOverrides();
        host.UndoZones();

        Shape(host).ShouldBe(edited);
    }

    [Fact]
    public void ResettingTheKeysLeavesTheZonesAlone()
    {
        // The two resets are separate answers to "put it back". Someone who has
        // spent time shaping zones must not lose that by tidying up their keys.
        using var host = Host("single-32-9");

        var slot = host.GetCustomizations()[0].Slot;
        host.SetDisplayColumns(slot, 5);

        var shaped = Shape(host);

        host.ResetLayout();

        Shape(host).ShouldBe(shaped);
    }
}
