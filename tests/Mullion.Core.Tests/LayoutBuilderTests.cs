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
    public void VerticalsFlankingStackedPairProduceNineKeysWithACentreUnion()
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

        Project(r, 0, 1, work).ShouldBe(new PxRect(1280, 0, 2560, 696));
        Project(r, 2, 1, work).ShouldBe(new PxRect(1280, 696, 2560, 696));
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

    // ---- The cross-monitor union guard ------------------------------------

    /// <summary>
    /// The regression net. Two ordinary side-by-side monitors must NOT silently
    /// gain a bezel-spanning zone - that behavior appearing in the most common
    /// setup on earth is exactly what the clean-rectangle guard prevents.
    /// </summary>
    [Fact]
    public void TwoSideBySideMonitorsGetNoSpanningZone()
    {
        var r = LayoutBuilder.Build(TestDisplays.TwoAcross(), Left);

        r.Zones.ShouldAllBe(z => !z.SpansDisplays);
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
