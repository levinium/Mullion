using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class DisplayGridTests
{
    /// <summary>
    /// The regression that motivated per-column analysis. Global union-find over
    /// displays collapses this arrangement into a single band, because each
    /// vertical overlaps both center monitors. Per-column analysis must report
    /// 3 columns with depths 1, 2, 1.
    /// </summary>
    [Fact]
    public void VerticalsFlankingStackedPairKeepsTheCentreColumnTwoDeep()
    {
        var columns = DisplayGrid.Columns(TestDisplays.VerticalsFlankingStackedPair());

        columns.Count.ShouldBe(3);
        columns[0].Depth.ShouldBe(1);
        columns[1].Depth.ShouldBe(2);
        columns[2].Depth.ShouldBe(1);

        columns[0].Displays[0].StableKey.ShouldBe("LV");
        columns[1].Displays[0].StableKey.ShouldBe("CU");   // upper first
        columns[1].Displays[1].StableKey.ShouldBe("CL");
        columns[2].Displays[0].StableKey.ShouldBe("RV");

        DisplayGrid.RequiredRows(columns).ShouldBe(2);
    }

    [Fact]
    public void VerticalsFlankingOneLandscapeIsThreeColumnsOneDeep()
    {
        var columns = DisplayGrid.Columns(TestDisplays.VerticalsFlankingOneLandscape());

        columns.Count.ShouldBe(3);
        columns.ShouldAllBe(c => c.Depth == 1);
        DisplayGrid.RequiredRows(columns).ShouldBe(1);
    }

    [Fact]
    public void OrdersColumnsLeftToRightIncludingNegativeOrigins()
    {
        var columns = DisplayGrid.Columns(TestDisplays.ThreeAcross());

        columns.Select(c => c.Displays[0].StableKey).ShouldBe(["L", "C", "R"]);
    }

    [Fact]
    public void StackedMonitorsAreOneColumnTwoDeep()
    {
        var columns = DisplayGrid.Columns(TestDisplays.TwoStacked());

        columns.Count.ShouldBe(1);
        columns[0].Depth.ShouldBe(2);
        columns[0].Displays[0].StableKey.ShouldBe("TOP");
    }

    [Fact]
    public void TwoByTwoIsTwoColumnsTwoDeep()
    {
        var columns = DisplayGrid.Columns(TestDisplays.TwoByTwo());

        columns.Count.ShouldBe(2);
        columns.ShouldAllBe(c => c.Depth == 2);
        columns[0].Displays.Select(d => d.StableKey).ShouldBe(["TL", "BL"]);
        columns[1].Displays.Select(d => d.StableKey).ShouldBe(["TR", "BR"]);
    }

    [Fact]
    public void StaggeredHeightsStillClusterIntoOneRowPerColumn()
    {
        // A 1440p beside a 1080p, bottom-aligned - a very common real desk.
        var displays = new List<Mullion.Core.Model.DisplayInfo>
        {
            TestDisplays.At(0, 0, 2560, 1440, primary: true, key: "BIG"),
            TestDisplays.At(2560, 360, 1920, 1080, key: "SMALL"),
        };

        var columns = DisplayGrid.Columns(displays);

        columns.Count.ShouldBe(2);
        columns.ShouldAllBe(c => c.Depth == 1);
    }
}

public class CleanRectangleTests
{
    /// <summary>The 4-monitor case: the center pair may bind a spanning union key.</summary>
    [Fact]
    public void StackedSameWidthSameDpiFormsACleanRectangle()
    {
        var columns = DisplayGrid.Columns(TestDisplays.VerticalsFlankingStackedPair());

        DisplayGrid.FormsCleanRectangle(columns[1].Displays).ShouldBeTrue();
    }

    [Fact]
    public void SideBySideSameSizeFormsACleanRectangle()
    {
        var d = TestDisplays.TwoAcross();

        DisplayGrid.FormsCleanRectangle(d[0], d[1]).ShouldBeTrue();
    }

    /// <summary>
    /// The guard that stops the union rule misfiring. A window spanning two
    /// scaling factors takes the DPI of whichever monitor holds its majority and
    /// renders wrong on the other, so mixed DPI must never form a union.
    /// </summary>
    [Fact]
    public void MixedDpiNeverFormsACleanRectangle()
    {
        var d = TestDisplays.MixedDpi();

        DisplayGrid.FormsCleanRectangle(d[0], d[1]).ShouldBeFalse();
    }

    [Fact]
    public void MismatchedSizesDoNotFormACleanRectangle()
    {
        var big = TestDisplays.At(0, 0, 2560, 1440, key: "BIG");
        var small = TestDisplays.At(2560, 0, 1920, 1080, key: "SMALL");

        DisplayGrid.FormsCleanRectangle(big, small).ShouldBeFalse();
    }

    [Fact]
    public void GappedMonitorsDoNotFormACleanRectangle()
    {
        var a = TestDisplays.At(0, 0, 2560, 1440, key: "A");
        var b = TestDisplays.At(2900, 0, 2560, 1440, key: "B");   // 340px gap

        DisplayGrid.FormsCleanRectangle(a, b).ShouldBeFalse();
    }

    [Fact]
    public void SingleDisplayIsTriviallyClean()
    {
        DisplayGrid.FormsCleanRectangle(TestDisplays.SuperUltrawideAlone()).ShouldBeTrue();
    }
}
