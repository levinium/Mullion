using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class DisplayOverrideTests
{
    private static readonly KeySurface Left = KeySurface.LeftHandBlock;

    private static PxRect Zone(LayoutResult r, int row, int col, PxRect work) =>
        r.At(row, col)!.Parts[0].Area.Project(work);

    // ---- Counts -----------------------------------------------------------

    [Fact]
    public void AnExplicitCountReplacesTheDerivedOne()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var slot = DisplaySlot.Of(displays[0]);

        var derived = LayoutBuilder.Build(displays, Left);
        derived.Zones.Count(z => z.Position.Row == Left.HomeRow).ShouldBe(3);

        var custom = LayoutBuilder.Build(
            displays, Left, overrides: [new DisplayOverride(slot, Columns: 4)]);

        custom.Zones.Count(z => z.Position.Row == Left.HomeRow).ShouldBe(4);
    }

    /// <summary>
    /// Coarsening exists to fit the key surface. It must not quietly undo a
    /// count somebody set by hand - the whole point of setting it is that the
    /// derived answer was not wanted.
    /// </summary>
    [Fact]
    public void CoarseningLeavesAnExplicitCountAlone()
    {
        var displays = TestDisplays.StandardPlusSuperUltrawide();
        var wide = displays.MaxBy(d => d.Bounds.Width)!;

        var r = LayoutBuilder.Build(
            displays, Left, overrides: [new DisplayOverride(DisplaySlot.Of(wide), Columns: 4)]);

        r.Zones.Count(z => z.Position.Row == Left.HomeRow && z.Parts[0].DisplayKey == wide.StableKey)
            .ShouldBe(4);
    }

    // ---- Weights ----------------------------------------------------------

    [Fact]
    public void ExplicitWeightsSetTheSplit()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var work = displays[0].WorkArea;

        var r = LayoutBuilder.Build(displays, Left, overrides:
            [new DisplayOverride(DisplaySlot.Of(displays[0]), Columns: 3, Weights: [30, 40, 30])]);

        Zone(r, Left.HomeRow, 0, work).Width.ShouldBe(1536);   // 30% of 5120
        Zone(r, Left.HomeRow, 1, work).Width.ShouldBe(2048);   // 40%
        Zone(r, Left.HomeRow, 2, work).Width.ShouldBe(1536);
    }

    [Fact]
    public void WeightsAreRelativeSoAnyScaleWorks()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var work = displays[0].WorkArea;
        var slot = DisplaySlot.Of(displays[0]);

        var asPercent = LayoutBuilder.Build(displays, Left, overrides:
            [new DisplayOverride(slot, 3, [30, 40, 30])]);

        var asRatio = LayoutBuilder.Build(displays, Left, overrides:
            [new DisplayOverride(slot, 3, [3, 4, 3])]);

        Zone(asPercent, Left.HomeRow, 1, work).ShouldBe(Zone(asRatio, Left.HomeRow, 1, work));
    }

    /// <summary>
    /// Weights saved for a different number of zones describe a split that no
    /// longer exists. Applying the ones that happen to fit would leave a layout
    /// half-remembered and half-derived, which is worse than deriving it.
    /// </summary>
    [Fact]
    public void WeightsForTheWrongCountAreIgnored()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var work = displays[0].WorkArea;

        var stale = LayoutBuilder.Build(displays, Left, overrides:
            [new DisplayOverride(DisplaySlot.Of(displays[0]), Columns: 3, Weights: [1, 1])]);

        var derived = LayoutBuilder.Build(displays, Left);

        Zone(stale, Left.HomeRow, 1, work).ShouldBe(Zone(derived, Left.HomeRow, 1, work));
    }

    // ---- Which display an override belongs to -----------------------------

    /// <summary>
    /// The behaviour asked for explicitly: swap a monitor for a different one of
    /// the same size in the same place and the customisation stays, because it
    /// describes the desk rather than the panel.
    /// </summary>
    [Fact]
    public void ADifferentMonitorOfTheSameShapeAndPlaceKeepsTheOverride()
    {
        var before = TestDisplays.SuperUltrawideAlone();
        var slot = DisplaySlot.Of(before[0]);

        var after = new List<DisplayInfo>
        {
            TestDisplays.At(0, 0, 5120, 1440, primary: true, key: "SOME-OTHER-PANEL", taskbar: 48),
        };

        DisplaySlot.Of(after[0]).ShouldBe(slot);

        var r = LayoutBuilder.Build(after, Left, overrides: [new DisplayOverride(slot, Columns: 4)]);
        r.Zones.Count(z => z.Position.Row == Left.HomeRow).ShouldBe(4);
    }

    [Fact]
    public void MovingOrResizingADisplayDropsTheOverride()
    {
        var slot = DisplaySlot.Of(TestDisplays.SuperUltrawideAlone()[0]);

        var moved = new List<DisplayInfo> { TestDisplays.At(-1920, 0, 5120, 1440, primary: true, taskbar: 48) };
        var resized = new List<DisplayInfo> { TestDisplays.At(0, 0, 3440, 1440, primary: true, taskbar: 48) };

        DisplaySlot.Of(moved[0]).ShouldNotBe(slot);
        DisplaySlot.Of(resized[0]).ShouldNotBe(slot);
    }

    /// <summary>A taskbar that moves or hides must not discard a hand-made layout.</summary>
    [Fact]
    public void TheTaskbarDoesNotAffectWhichSlotADisplayIs()
    {
        var withBar = TestDisplays.At(0, 0, 5120, 1440, primary: true, taskbar: 48);
        var without = TestDisplays.At(0, 0, 5120, 1440, primary: true, taskbar: 0);

        DisplaySlot.Of(without).ShouldBe(DisplaySlot.Of(withBar));
    }

    // ---- Reverting --------------------------------------------------------

    [Fact]
    public void RemovingTheOverrideRestoresTheDerivedLayout()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var work = displays[0].WorkArea;
        var slot = DisplaySlot.Of(displays[0]);

        var custom = LayoutBuilder.Build(displays, Left, overrides: [new DisplayOverride(slot, 3, [30, 40, 30])]);
        var reverted = LayoutBuilder.Build(displays, Left, overrides: []);
        var never = LayoutBuilder.Build(displays, Left);

        Zone(custom, Left.HomeRow, 1, work).ShouldNotBe(Zone(never, Left.HomeRow, 1, work));
        Zone(reverted, Left.HomeRow, 1, work).ShouldBe(Zone(never, Left.HomeRow, 1, work));
    }

    [Fact]
    public void AnEmptyOverrideChangesNothing()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var work = displays[0].WorkArea;

        var r = LayoutBuilder.Build(displays, Left, overrides: [new DisplayOverride(DisplaySlot.Of(displays[0]))]);
        var derived = LayoutBuilder.Build(displays, Left);

        Zone(r, Left.HomeRow, 1, work).ShouldBe(Zone(derived, Left.HomeRow, 1, work));
    }

    /// <summary>An override for a display that is not here must not disturb the ones that are.</summary>
    [Fact]
    public void AnOverrideForAnAbsentDisplayIsIgnored()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var work = displays[0].WorkArea;

        var r = LayoutBuilder.Build(displays, Left, overrides:
            [new DisplayOverride("1920x1080@9999,9999", Columns: 5)]);

        Zone(r, Left.HomeRow, 1, work).ShouldBe(Zone(LayoutBuilder.Build(displays, Left), Left.HomeRow, 1, work));
    }

    /// <summary>Zones must still tile the work area exactly, whatever weights are given.</summary>
    [Fact]
    public void CustomWeightsStillTileWithoutGapsOrOverlap()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var work = displays[0].WorkArea;

        var r = LayoutBuilder.Build(displays, Left, overrides:
            [new DisplayOverride(DisplaySlot.Of(displays[0]), 3, [17, 53, 30])]);

        var row = Enumerable.Range(0, 3).Select(c => Zone(r, Left.HomeRow, c, work)).ToList();

        row[0].Left.ShouldBe(work.Left);
        row[0].Right.ShouldBe(row[1].Left);
        row[1].Right.ShouldBe(row[2].Left);
        row[2].Right.ShouldBe(work.Right);
    }
}
