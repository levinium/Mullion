using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class DropTargetsTests
{
    private static readonly KeySurface Left = KeySurface.LeftHandBlock;

    private static IReadOnlyList<DropTarget> For(IReadOnlyList<DisplayInfo> displays) =>
        DropTargets.Build(LayoutBuilder.Build(displays, Left), displays);

    /// <summary>
    /// The property the whole thing rests on: a pointer sits at one point, so
    /// the targets it might land in must not overlap. Zones themselves do.
    /// </summary>
    [Fact]
    public void TargetsNeverOverlap()
    {
        foreach (var displays in AllArrangements())
        {
            var targets = For(displays);

            for (var i = 0; i < targets.Count; i++)
            for (var j = i + 1; j < targets.Count; j++)
            {
                targets[i].Bounds.Intersects(targets[j].Bounds)
                    .ShouldBeFalse($"{targets[i].Zone.Name} overlaps {targets[j].Zone.Name}");
            }
        }
    }

    /// <summary>Every point of every work area must land in some target.</summary>
    [Fact]
    public void TargetsCoverEveryWorkArea()
    {
        foreach (var displays in AllArrangements())
        {
            var targets = For(displays);

            foreach (var display in displays)
            {
                var work = display.WorkArea;

                foreach (var (x, y) in new[]
                         {
                             (work.Left + 1, work.Top + 1),
                             (work.Right - 1, work.Top + 1),
                             (work.Left + 1, work.Bottom - 1),
                             (work.Right - 1, work.Bottom - 1),
                             (work.Left + work.Width / 2, work.Top + work.Height / 2),
                         })
                {
                    DropTargets.HitTest(targets, x, y)
                        .ShouldNotBeNull($"{display.FriendlyName} has a hole at {x},{y}");
                }
            }
        }
    }

    /// <summary>
    /// On the 32:9 the whole-column zones win over their own halves, so a drag
    /// offers the 25/50/25 split rather than six stacked quarters.
    /// </summary>
    [Fact]
    public void SuperUltrawideOffersTheThreeColumns()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var targets = For(displays);

        targets.Count.ShouldBe(3);
        targets.Select(t => t.Bounds.Width).OrderBy(w => w).ShouldBe([1280, 1280, 2560]);
        targets.ShouldAllBe(t => t.Bounds.Height == 1392);
    }

    /// <summary>
    /// A column with no whole-column zone keeps the parts that tile it - here a
    /// portrait 32:9 split into three stacked zones, none of which contains
    /// another.
    /// </summary>
    [Fact]
    public void StackedZonesAreAllKeptWhenNoneContainsAnother()
    {
        var displays = new List<DisplayInfo> { TestDisplays.At(0, 0, 1440, 5120, primary: true) };
        var targets = For(displays);

        targets.Count.ShouldBe(3);
        targets.Select(t => t.Bounds.Top).OrderBy(t => t).ShouldBe(
            [.. targets.Select(t => t.Bounds.Top).OrderBy(t => t)]);
    }

    /// <summary>
    /// The span across two monitors must not become a drop target: it covers
    /// both, so taken biggest-first it would swallow them and leave the desk
    /// with a single place to drop anything.
    /// </summary>
    [Fact]
    public void TheSpanAcrossDisplaysIsNotADropTarget()
    {
        var displays = TestDisplays.TwoAcross();
        var targets = For(displays);

        targets.Count.ShouldBe(2);
        targets.ShouldAllBe(t => t.Zone.Kind != ZoneKind.Union);
        targets.ShouldAllBe(t => !t.Zone.SpansDisplays);
    }

    [Fact]
    public void HitTestFindsTheZoneUnderThePoint()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var targets = For(displays);

        DropTargets.HitTest(targets, 100, 100)!.Bounds.ShouldBe(new PxRect(0, 0, 1280, 1392));
        DropTargets.HitTest(targets, 2000, 100)!.Bounds.ShouldBe(new PxRect(1280, 0, 2560, 1392));
        DropTargets.HitTest(targets, 5000, 100)!.Bounds.ShouldBe(new PxRect(3840, 0, 1280, 1392));
    }

    /// <summary>Below the work area is the taskbar, which is nobody's zone.</summary>
    [Fact]
    public void HitTestMissesOutsideEveryZone()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var targets = For(displays);

        DropTargets.HitTest(targets, 100, 1420).ShouldBeNull();
        DropTargets.HitTest(targets, -50, 100).ShouldBeNull();
    }

    private static IEnumerable<List<DisplayInfo>> AllArrangements() =>
    [
        TestDisplays.SuperUltrawideAlone(),
        TestDisplays.TwoAcross(),
        TestDisplays.ThreeAcross(),
        TestDisplays.ThreePortraitAcross(),
        TestDisplays.VerticalsFlankingStackedPair(),
        TestDisplays.VerticalsFlankingOneLandscape(),
        TestDisplays.StandardPlusSuperUltrawide(),
        TestDisplays.TwoStacked(),
        TestDisplays.TwoByTwo(),
        TestDisplays.MixedDpi(),
    ];
}
