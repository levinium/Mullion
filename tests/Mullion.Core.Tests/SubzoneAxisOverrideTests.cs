using Mullion.Core.Hotkeys;
using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// Choosing a zone's subzone axis by hand, against the shape rule that would
/// otherwise pick it.
/// <para>
/// The rule answers a question about shape, and which way to cut a zone is a
/// preference as much as a measurement: a zone can be exactly the right shape for
/// side-by-side halves and still be the place someone always wants one window
/// above another. So the derived answer is a default, not a verdict.
/// </para>
/// </summary>
public class SubzoneAxisOverrideTests
{
    private const string Slot = "5120x1440@0,0";

    /// <summary>The centre zone's first half, which the shape rule cuts side by side.</summary>
    private static readonly GridPos CentreFirst = new(0, 1);

    /// <summary>A side zone's first half, which the shape rule stacks.</summary>
    private static readonly GridPos SideFirst = new(0, 0);

    private static LayoutResult Build(params string?[] axes) =>
        LayoutBuilder.Build(
            TestDisplays.SuperUltrawideAlone(),
            overrides: [new DisplayOverride(Slot, SubzoneAxes: axes)]);

    private static PxRect Of(LayoutResult r, GridPos at) =>
        r.At(at)!.Parts[0].Area.Project(TestDisplays.SuperUltrawideAlone()[0].WorkArea);

    [Fact]
    public void NothingChosenLeavesTheShapeRuleInCharge()
    {
        var derived = LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());
        var blank = Build(null, null, null);

        Of(blank, CentreFirst).ShouldBe(Of(derived, CentreFirst));
        Of(blank, SideFirst).ShouldBe(Of(derived, SideFirst));
    }

    [Fact]
    public void StackingCanBeAskedForWhereTheShapeSaysOtherwise()
    {
        // The centre is 2560x1392 and the rule splits it side by side. Asked to
        // stack, it stacks - even though that produces the 3.68 aspect the rule
        // exists to avoid, because the person asking can see their own desk.
        var pinned = Build(null, DisplayOverride.Stacked, null);

        Of(pinned, CentreFirst).ShouldBe(new PxRect(1280, 0, 2560, 696));
    }

    [Fact]
    public void SideBySideCanBeAskedForToo()
    {
        // The left column is 1280x1392 and the rule stacks it; asked for halves
        // beside each other, they are 640x1392.
        var pinned = Build(DisplayOverride.SideBySide, null, null);

        Of(pinned, SideFirst).ShouldBe(new PxRect(0, 0, 640, 1392));
    }

    [Fact]
    public void ChoosingOneZoneLeavesTheOthersDerived()
    {
        // The list is per zone, so pinning the centre must not quietly pin the
        // sides to the same thing.
        var derived = LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());
        var pinned = Build(null, DisplayOverride.Stacked, null);

        Of(pinned, SideFirst).ShouldBe(Of(derived, SideFirst));
        Of(pinned, new GridPos(0, 2)).ShouldBe(Of(derived, new GridPos(0, 2)));
    }

    [Fact]
    public void ASecondHalfFollowsTheFirst()
    {
        // Both halves are one decision. Cutting the first side by side and the
        // second stacked would not be a split at all.
        var pinned = Build(null, DisplayOverride.Stacked, null);

        Of(pinned, new GridPos(2, 1)).ShouldBe(new PxRect(1280, 696, 2560, 696));
    }

    [Fact]
    public void AListShorterThanTheZonesPinsOnlyWhatItNames()
    {
        // The zone count can change after these are saved. A short list must not
        // throw and must not shift onto the wrong zone.
        var derived = LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());
        var pinned = Build(DisplayOverride.SideBySide);

        Of(pinned, SideFirst).ShouldBe(new PxRect(0, 0, 640, 1392));
        Of(pinned, CentreFirst).ShouldBe(Of(derived, CentreFirst));
    }

    [Theory]
    [InlineData("horizontal")]
    [InlineData("Stacked")]
    [InlineData("")]
    [InlineData("whatever a later build writes here")]
    public void AWordNobodyRecognisesMeansDeriveIt(string word)
    {
        // Written by a newer build, or by hand. Falling back to the shape rule
        // keeps one unreadable entry from failing the whole load.
        var derived = LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());

        Of(Build(word, word, word), CentreFirst).ShouldBe(Of(derived, CentreFirst));
    }

    [Fact]
    public void AnOverrideThatChoosesNothingIsEmpty()
    {
        // IsEmpty decides whether "reset zones to default" is offered, so a list
        // of nulls has to read as no customization rather than as one.
        new DisplayOverride(Slot, SubzoneAxes: [null, null]).IsEmpty.ShouldBeTrue();
        new DisplayOverride(Slot, SubzoneAxes: ["nonsense"]).IsEmpty.ShouldBeTrue();
        new DisplayOverride(Slot, SubzoneAxes: [DisplayOverride.Stacked]).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void TheWordsRoundTrip()
    {
        DisplayOverride.Word(Axis.Vertical).ShouldBe(DisplayOverride.Stacked);
        DisplayOverride.Word(Axis.Horizontal).ShouldBe(DisplayOverride.SideBySide);
        DisplayOverride.Word(null).ShouldBeNull();

        new DisplayOverride(Slot, SubzoneAxes: [DisplayOverride.Word(Axis.Horizontal)])
            .AxisFor(0).ShouldBe(Axis.Horizontal);
    }
}
