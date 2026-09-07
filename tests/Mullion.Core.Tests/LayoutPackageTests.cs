using Mullion.Core.Config;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class LayoutPackageTests
{
    private static readonly DisplayOverride Wide = new("5120x1440@0,0", 3, [30, 40, 30]);
    private static readonly DisplayOverride Side = new("1920x1080@5120,0", 2);

    [Fact]
    public void RoundTripsThroughJson()
    {
        var exported = LayoutPackageIo.Export([Wide, Side], ["5120x1440@0,0"], "left-hand", "Desk");
        var back = LayoutPackageIo.Deserialize(LayoutPackageIo.Serialize(exported), out var error);

        error.ShouldBeNull();
        back.ShouldNotBeNull();
        back.Name.ShouldBe("Desk");
        back.SurfaceId.ShouldBe("left-hand");
        back.Overrides.Count.ShouldBe(2);
        back.Overrides.First(o => o.Slot == Wide.Slot).Weights.ShouldBe([30, 40, 30]);
        back.Overrides.First(o => o.Slot == Side.Slot).Columns.ShouldBe(2);
    }

    [Fact]
    public void ExportSkipsOverridesThatSayNothing()
    {
        var package = LayoutPackageIo.Export([Wide, new DisplayOverride("empty")], [], "left-hand", "x");

        package.Overrides.Count.ShouldBe(1);
        package.Overrides[0].Slot.ShouldBe(Wide.Slot);
    }

    // ---- Reading files that are not what we hoped --------------------------

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"nope\": true }")]
    [InlineData("")]
    public void UnreadableFilesReportRatherThanThrow(string json)
    {
        LayoutPackageIo.Deserialize(json, out var error).ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A file from a future build is refused with its version named, rather than
    /// half-read into something that looks right and is not.
    /// </summary>
    [Fact]
    public void AFileFromANewerVersionIsRefused()
    {
        var json = LayoutPackageIo.Serialize(
            LayoutPackageIo.Export([Wide], [], "left-hand", "x") with { Version = 99 });

        LayoutPackageIo.Deserialize(json, out var error).ShouldBeNull();
        error.ShouldNotBeNull();
        error.ShouldContain("newer version");
    }

    // ---- What an import would do ------------------------------------------

    [Fact]
    public void PreviewSeparatesWhatAppliesFromWhatDoesNot()
    {
        var package = LayoutPackageIo.Export([Wide, Side], [], "left-hand", "x");
        var preview = LayoutPackageIo.Preview(package, ["5120x1440@0,0"]);

        preview.Matching.ShouldBe([Wide.Slot]);
        preview.Missing.ShouldBe([Side.Slot]);
        preview.AnythingApplies.ShouldBeTrue();
    }

    [Fact]
    public void PreviewSaysSoWhenNothingWouldApply()
    {
        var package = LayoutPackageIo.Export([Wide], [], "left-hand", "x");

        LayoutPackageIo.Preview(package, ["1280x1024@0,0"]).AnythingApplies.ShouldBeFalse();
    }

    // ---- Merging -----------------------------------------------------------

    /// <summary>
    /// Two overrides for one slot is a contradiction, and the imported one is
    /// the one just asked for.
    /// </summary>
    [Fact]
    public void ImportReplacesBySlotRatherThanAppending()
    {
        var existing = new DisplayOverride(Wide.Slot, 2, [1, 1]);
        var merged = LayoutPackageIo.Merge([existing], [Wide]);

        merged.Count.ShouldBe(1);
        merged[0].Weights.ShouldBe([30, 40, 30]);
    }

    /// <summary>
    /// An override for a display that is not here is kept, not dropped: plugging
    /// that monitor back in should bring its layout with it.
    /// </summary>
    [Fact]
    public void MergeKeepsOverridesForDisplaysThatAreNotHere()
    {
        var merged = LayoutPackageIo.Merge([Side], [Wide]);

        merged.Select(o => o.Slot).ShouldBe([Side.Slot, Wide.Slot], ignoreOrder: true);
    }
}
