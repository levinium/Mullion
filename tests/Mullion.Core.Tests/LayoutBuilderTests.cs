using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class LayoutBuilderTests
{
    private static readonly KeySurface Left = KeySurface.LeftHandBlock;

    /// <summary>Label a zone by its QWERTY key, for readable assertions.</summary>
    private static string KeyOf(LayoutResult r, int row, int col) =>
        r.At(row, col) is null ? "-" : r.Surface.FallbackLabelAt(new GridPos(row, col));

    private static string Map(LayoutResult r) =>
        string.Join(" / ", Enumerable.Range(0, r.Surface.Rows)
            .Select(row => string.Concat(Enumerable.Range(0, r.Surface.Cols)
                .Select(col => KeyOf(r, row, col)))));

    // ---- The cases from the original brief ---------------------------------

    [Fact]
    public void ThreeMonitorsGiveASDOnTheHomeRow()
    {
        var r = LayoutBuilder.Build(TestDisplays.ThreeAcross(), Left);

        r.At(1, 0)!.Kind.ShouldBe(ZoneKind.WholeDisplay);
        r.At(1, 1)!.Kind.ShouldBe(ZoneKind.WholeDisplay);
        r.At(1, 2)!.Kind.ShouldBe(ZoneKind.WholeDisplay);
        Map(r).ShouldBe("QWE-- / ASD-- / ZXC--");
    }

    /// <summary>
    /// The user's nine-key case: three vertical monitors, each getting
    /// upper half / whole display / lower half rather than just two halves.
    /// </summary>
    [Fact]
    public void ThreePortraitMonitorsSpendTheWholeBudget()
    {
        var r = LayoutBuilder.Build(TestDisplays.ThreePortraitAcross(), Left);

        Map(r).ShouldBe("QWE-- / ASD-- / ZXC--");

        r.At(1, 0)!.Kind.ShouldBe(ZoneKind.WholeDisplay);
        r.At(0, 0)!.Name.ShouldEndWith("upper");
        r.At(2, 0)!.Name.ShouldEndWith("lower");
    }

    /// <summary>
    /// The 4-display arrangement that broke global band clustering. Verticals get
    /// three tiers each; the center column's two monitors take the outer rows with
    /// the union on the home row.
    /// </summary>
    [Fact]
    public void VerticalsFlankingStackedPairProduceNineKeysWithACenterUnion()
    {
        var r = LayoutBuilder.Build(TestDisplays.VerticalsFlankingStackedPair(), Left);

        Map(r).ShouldBe("QWE-- / ASD-- / ZXC--");

        // Verticals: three tiers, whole display on the home row.
        r.At(1, 0)!.Kind.ShouldBe(ZoneKind.WholeDisplay);
        r.At(1, 2)!.Kind.ShouldBe(ZoneKind.WholeDisplay);

        // Center column: displays on the outer rows, union in the middle.
        r.At(0, 1)!.Parts[0].DisplayKey.ShouldBe("CU");
        r.At(2, 1)!.Parts[0].DisplayKey.ShouldBe("CL");

        var union = r.At(1, 1)!;
        union.Kind.ShouldBe(ZoneKind.Union);
        union.SpansDisplays.ShouldBeTrue();
        union.Parts.Select(p => p.DisplayKey).ShouldBe(["CU", "CL"], ignoreOrder: true);
    }

    [Fact]
    public void VerticalsFlankingOneLandscapeGiveEveryColumnTiers()
    {
        var r = LayoutBuilder.Build(TestDisplays.VerticalsFlankingOneLandscape(), Left);

        Map(r).ShouldBe("QWE-- / ASD-- / ZXC--");
        r.Zones.Count(z => z.Kind == ZoneKind.WholeDisplay).ShouldBe(3);
        r.Zones.ShouldAllBe(z => !z.SpansDisplays);
    }

    /// <summary>The "standard + ultrawide" case: 1 + 3 columns, not 2 + 2.</summary>
    [Fact]
    public void StandardPlusSuperUltrawideAllocatesByDemand()
    {
        var r = LayoutBuilder.Build(TestDisplays.StandardPlusSuperUltrawide(), Left);

        r.At(1, 0)!.Parts[0].DisplayKey.ShouldBe("STD");
        r.At(1, 0)!.Kind.ShouldBe(ZoneKind.WholeDisplay);

        r.At(1, 1)!.Parts[0].DisplayKey.ShouldBe("UW");
        r.At(1, 2)!.Parts[0].DisplayKey.ShouldBe("UW");
        r.At(1, 3)!.Parts[0].DisplayKey.ShouldBe("UW");
        r.At(1, 4).ShouldBeNull();
    }

    /// <summary>The development machine: 25/50/25 on the home row, halves above and below.</summary>
    [Fact]
    public void SingleSuperUltrawideGivesNineRegions()
    {
        var displays = TestDisplays.SuperUltrawideAlone();
        var r = LayoutBuilder.Build(displays, Left);
        var work = displays[0].WorkArea;

        Map(r).ShouldBe("QWE-- / ASD-- / ZXC--");

        Project(r, 1, 0, work).ShouldBe(new PxRect(0, 0, 1280, 1392));
        Project(r, 1, 1, work).ShouldBe(new PxRect(1280, 0, 2560, 1392));
        Project(r, 1, 2, work).ShouldBe(new PxRect(3840, 0, 1280, 1392));

        // Subzones cut whichever way leaves two usable windows, so this one desk
        // answers both ways at once - which is the case that rules out any global
        // "tier direction" setting.
        //
        // The centre is 2560x1392. Stacked it would give 2560x696, an aspect of
        // 3.68 against a ZoneAspectMax of 2.20 - a pair of letterboxes the engine
        // refuses outright when they are zones. Side by side gives 1280x1392.
        Project(r, 0, 1, work).ShouldBe(new PxRect(1280, 0, 1280, 1392));
        Project(r, 2, 1, work).ShouldBe(new PxRect(2560, 0, 1280, 1392));

        // The sides are 1280x1392, where the answer is the other one: stacked
        // gives 1280x696 (1.84, inside the band) and side by side would give
        // 640x1392 (0.46, below the 0.62 floor).
        Project(r, 0, 0, work).ShouldBe(new PxRect(0, 0, 1280, 696));
        Project(r, 2, 0, work).ShouldBe(new PxRect(0, 696, 1280, 696));

        Project(r, 0, 2, work).ShouldBe(new PxRect(3840, 0, 1280, 696));
        Project(r, 2, 2, work).ShouldBe(new PxRect(3840, 696, 1280, 696));
    }

    private static PxRect Project(LayoutResult r, int row, int col, PxRect work) =>
        r.At(row, col)!.Parts[0].Area.Project(work);

    // ---- Home-row invariance ----------------------------------------------

    /// <summary>
    /// The guarantee that makes tiers safe to add. Across every arrangement, the
    /// home row addresses the columns themselves - so a user who learned A/S/D
    /// never has it move under them.
    /// </summary>
    [Fact]
    public void HomeRowAlwaysAddressesTheColumnsThemselves()
    {
        var arrangements = new (string Name, List<DisplayInfo> Displays)[]
        {
            ("single 32:9", TestDisplays.SuperUltrawideAlone()),
            ("two across", TestDisplays.TwoAcross()),
            ("three across", TestDisplays.ThreeAcross()),
            ("three portrait", TestDisplays.ThreePortraitAcross()),
            ("verticals + stacked pair", TestDisplays.VerticalsFlankingStackedPair()),
            ("verticals + landscape", TestDisplays.VerticalsFlankingOneLandscape()),
            ("standard + 32:9", TestDisplays.StandardPlusSuperUltrawide()),
            ("two stacked", TestDisplays.TwoStacked()),
            ("2x2", TestDisplays.TwoByTwo()),
            ("mixed dpi", TestDisplays.MixedDpi()),
        };

        foreach (var (name, displays) in arrangements)
        {
            var r = LayoutBuilder.Build(displays, Left);
            var columnsUsed = r.Zones.Select(z => z.Position.Col).Distinct().OrderBy(c => c);

            foreach (var col in columnsUsed)
            {
                var atHome = r.At(Left.HomeRow, col);
                atHome.ShouldNotBeNull($"{name}: home row column {col} must be bound");
                atHome.Kind.ShouldBeOneOf(ZoneKind.WholeDisplay, ZoneKind.Region, ZoneKind.Union);
            }
        }
    }

    /// <summary>Every zone must land on a real surface cell, with no two sharing one.</summary>
    [Fact]
    public void ZonesNeverCollideOrEscapeTheSurface()
    {
        foreach (var displays in AllArrangements())
        {
            var r = LayoutBuilder.Build(displays, Left);

            r.Zones.ShouldAllBe(z => Left.Contains(z.Position));
            r.Zones.Select(z => z.Position).Distinct().Count().ShouldBe(r.Zones.Count);
        }
    }

    // ---- Two across take the outer keys, not the leftmost two --------------

    /// <summary>
    /// A lone 16:9 splits in two, and those halves belong on A and D - the
    /// left/right mnemonic - with S expanding to the whole monitor. Packing
    /// from the left would put the right half on S, which reads as "middle".
    /// </summary>
    [Fact]
    public void OneDisplaySplitInTwoTakesTheOuterKeysAndBindsTheWholeMonitorBetween()
    {
        var r = LayoutBuilder.Build([TestDisplays.At(0, 0, 2560, 1440, primary: true)], Left);
        var home = Left.HomeRow;

        r.Zones.Single(z => z.Position == new GridPos(home, 0)).Name.ShouldBe("Left");
        r.Zones.Single(z => z.Position == new GridPos(home, 2)).Name.ShouldBe("Right");

        var whole = r.Zones.Single(z => z.Position == new GridPos(home, 1));
        whole.Kind.ShouldBe(ZoneKind.Union);
        whole.SpansDisplays.ShouldBeFalse();
        whole.Parts.Single().Area.ShouldBe(NormRect.Full);
    }

    /// <summary>
    /// The span column spends its spare rows the way every other column does.
    /// Without this the middle column bound one key of three while each
    /// neighbor bound all three - the same surplus, treated inconsistently.
    /// </summary>
    [Fact]
    public void TheSpanColumnAlsoGetsUpperAndLowerHalves()
    {
        var displays = TestDisplays.TwoAcross();
        var r = LayoutBuilder.Build(displays, Left);
        var home = Left.HomeRow;

        var upper = r.At(home - 1, 1)!;
        var lower = r.At(home + 1, 1)!;

        upper.Kind.ShouldBe(ZoneKind.Union);
        lower.Kind.ShouldBe(ZoneKind.Union);
        upper.SpansDisplays.ShouldBeTrue();
        lower.SpansDisplays.ShouldBeTrue();

        // Each half covers the top or bottom of every display the span touches.
        var work = displays[0].WorkArea;
        var top = upper.Parts[0].Area.Project(work);
        var bottom = lower.Parts[0].Area.Project(work);

        top.Y.ShouldBe(work.Y);
        top.Height.ShouldBe(work.Height / 2);
        bottom.Bottom.ShouldBe(work.Bottom);
        bottom.Y.ShouldBe(top.Bottom);
    }

    /// <summary>
    /// The guard covers all three together: no span, no halves of it either.
    /// </summary>
    [Fact]
    public void NoSpanMeansNoSpanTiers()
    {
        var displays = new List<DisplayInfo>
        {
            TestDisplays.At(0, 0, 1280, 1024, key: "OLD"),
            TestDisplays.At(1280, 0, 2560, 1440, primary: true, key: "NEW"),
        };

        var r = LayoutBuilder.Build(displays, Left);

        r.Zones.ShouldAllBe(z => z.Position.Col != 1);
    }

    [Fact]
    public void TwoDisplaysTakeTheOuterKeys()
    {
        var r = LayoutBuilder.Build(TestDisplays.TwoAcross(), Left);
        var home = Left.HomeRow;

        r.Zones.ShouldContain(z => z.Position == new GridPos(home, 0));
        r.Zones.ShouldContain(z => z.Position == new GridPos(home, 2));
    }

    /// <summary>
    /// Three or more pack from the left with no gap, so the home row keeps
    /// meaning "the whole of this column" in every position.
    /// </summary>
    [Fact]
    public void ThreeAcrossPackWithNoGap()
    {
        var r = LayoutBuilder.Build(TestDisplays.ThreeAcross(), Left);
        var home = Left.HomeRow;

        for (var col = 0; col < 3; col++)
            r.Zones.ShouldContain(z => z.Position == new GridPos(home, col));

        r.Zones.ShouldAllBe(z => z.Kind != ZoneKind.Union);
    }

    // ---- The cross-monitor union guard ------------------------------------

    /// <summary>
    /// Two matched monitors side by side ARE a clean rectangle, so the key
    /// between them spans the pair. This is the one arrangement where a
    /// bezel-crossing zone is worth having, and it stays behind the toggle.
    /// </summary>
    [Fact]
    public void TwoMatchedMonitorsSideBySideSpanFromTheKeyBetweenThem()
    {
        var r = LayoutBuilder.Build(TestDisplays.TwoAcross(), Left);

        var span = r.Zones.Single(z => z.Position == new GridPos(Left.HomeRow, 1));
        span.SpansDisplays.ShouldBeTrue();
        span.Kind.ShouldBe(ZoneKind.Union);
    }

    [Fact]
    public void SpanningZonesCanBeTurnedOff()
    {
        var r = LayoutBuilder.Build(TestDisplays.TwoAcross(), Left, allowSpanningUnions: false);

        r.Zones.ShouldAllBe(z => !z.SpansDisplays);
    }

    /// <summary>
    /// The regression net. Mismatched monitors have no clean rectangle to span -
    /// the bounding box is a ragged shape with dead space in it - so the key
    /// between them stays unbound rather than binding something nobody wants.
    /// </summary>
    [Fact]
    public void MismatchedMonitorsSideBySideGetNoSpanningZone()
    {
        var displays = new List<DisplayInfo>
        {
            TestDisplays.At(0, 0, 1280, 1024, key: "OLD"),
            TestDisplays.At(1280, 0, 2560, 1440, primary: true, key: "NEW"),
        };

        var r = LayoutBuilder.Build(displays, Left);

        r.Zones.ShouldAllBe(z => !z.SpansDisplays);
        r.Notes.ShouldContain(n => n.Contains("clean rectangle"));
    }

    [Fact]
    public void MixedDpiStackWouldNotBindAUnion()
    {
        var displays = new List<DisplayInfo>
        {
            TestDisplays.At(0, 0, 2560, 1440, dpi: 96, key: "TOP"),
            TestDisplays.At(0, 1440, 2560, 1440, dpi: 144, primary: true, key: "BOTTOM"),
        };

        var r = LayoutBuilder.Build(displays, Left);

        r.Zones.ShouldAllBe(z => !z.SpansDisplays);
        r.Notes.ShouldContain(n => n.Contains("clean rectangle"));
    }

    [Fact]
    public void SpanningUnionsCanBeDisabled()
    {
        var r = LayoutBuilder.Build(
            TestDisplays.VerticalsFlankingStackedPair(), Left, allowSpanningUnions: false);

        r.Zones.ShouldAllBe(z => !z.SpansDisplays);
    }

    // ---- Surface indirection ----------------------------------------------

    /// <summary>
    /// A 3x3 arrangement on the numpad must produce the same GRID, just different
    /// scan codes - proving the allocator reads its shape from the surface.
    /// </summary>
    [Fact]
    public void SameArrangementMapsOntoTheNumpadSurface()
    {
        var displays = TestDisplays.ThreePortraitAcross();

        var left = LayoutBuilder.Build(displays, KeySurface.LeftHandBlock);
        var numpad = LayoutBuilder.Build(displays, KeySurface.Numpad);

        numpad.Zones.Select(z => z.Position).OrderBy(p => p.Row).ThenBy(p => p.Col)
            .ShouldBe(left.Zones.Select(z => z.Position).OrderBy(p => p.Row).ThenBy(p => p.Col));

        numpad.Surface.ScanCodeAt(new GridPos(1, 0)).ShouldBe((ushort)0x4B); // Num4
        left.Surface.ScanCodeAt(new GridPos(1, 0)).ShouldBe((ushort)0x1E);   // A
    }

    private static IEnumerable<List<DisplayInfo>> AllArrangements()
    {
        yield return TestDisplays.SuperUltrawideAlone();
        yield return TestDisplays.TwoAcross();
        yield return TestDisplays.ThreeAcross();
        yield return TestDisplays.ThreePortraitAcross();
        yield return TestDisplays.VerticalsFlankingStackedPair();
        yield return TestDisplays.VerticalsFlankingOneLandscape();
        yield return TestDisplays.StandardPlusSuperUltrawide();
        yield return TestDisplays.TwoStacked();
        yield return TestDisplays.TwoByTwo();
        yield return TestDisplays.MixedDpi();
    }
}
