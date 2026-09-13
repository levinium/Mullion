using Mullion.Core.Model;
using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// Binding a zone to a key the key surface has never heard of.
/// <para>
/// A surface is a set of DEFAULTS - a shape the allocator lays zones out on -
/// and it was also, for no reason anyone chose, a cage: rebinding refused
/// anything outside the fifteen letters, so reaching F5 or the tilde key meant
/// switching the whole desk to another surface. The zone keeps its place in the
/// grid, which is what the diagram draws and what identifies it across a
/// rebuild; only the key it answers to changes.
/// </para>
/// </summary>
public class AnyKeyBindingTests
{
    private const ushort ScanF5 = 0x3B + 4;   // F1 is 0x3B
    private const ushort ScanGrave = 0x29;
    private const ushort ScanA = 0x1E;

    private static LayoutResult Fresh() =>
        LayoutBuilder.Build(TestDisplays.SuperUltrawideAlone());

    private static Zone At(LayoutResult layout, GridPos where) =>
        layout.Zones.Single(z => z.Position == where);

    [Fact]
    public void AZoneCanBeMovedOntoAKeyOutsideTheSurface()
    {
        var layout = Fresh();
        var home = new GridPos(1, 0);

        var outcome = LayoutEditor.RebindToKey(layout, home, KeyStroke.Plain(ScanF5));

        outcome.Success.ShouldBeTrue();
        At(outcome.Layout, home).KeyOn(outcome.Layout.Surface)
            .ShouldBe(KeyStroke.Plain(ScanF5));
    }

    [Fact]
    public void ItKeepsItsPlaceOnTheDesk()
    {
        // Position is where the zone IS, not which key reaches it. Moving the key
        // must not move the zone in the diagram, or shuffle the grid the other
        // zones are laid out on.
        var layout = Fresh();
        var home = new GridPos(1, 1);
        var before = At(layout, home).Parts[0].Area;

        var outcome = LayoutEditor.RebindToKey(layout, home, KeyStroke.Plain(ScanGrave));

        At(outcome.Layout, home).Parts[0].Area.ShouldBe(before);
        outcome.Layout.Zones.Count.ShouldBe(layout.Zones.Count);
    }

    [Fact]
    public void EveryOtherZoneIsLeftAlone()
    {
        var layout = Fresh();

        var outcome = LayoutEditor.RebindToKey(layout, new GridPos(1, 0), KeyStroke.Plain(ScanF5));

        foreach (var zone in outcome.Layout.Zones.Where(z => z.Position != new GridPos(1, 0)))
            zone.Key.ShouldBeNull($"{zone.Name} was not the one being rebound");
    }

    [Fact]
    public void PuttingAZoneBackOnItsOwnKeyStoresNoOverride()
    {
        // Otherwise the zone would keep an override saying "use exactly the key
        // you would have used anyway", and then stop following a change of key
        // surface for no visible reason. The modifier already works this way.
        var layout = Fresh();
        var home = new GridPos(1, 0);
        var itsOwn = KeyStroke.Plain(layout.Surface.ScanCodeAt(home));

        var moved = LayoutEditor.RebindToKey(layout, home, KeyStroke.Plain(ScanF5)).Layout;
        At(moved, home).Key.ShouldNotBeNull();

        var back = LayoutEditor.RebindToKey(moved, home, itsOwn).Layout;
        At(back, home).Key.ShouldBeNull();
    }

    [Fact]
    public void TwoZonesOnOneKeySwapRatherThanOneDisappearing()
    {
        // The same rule an on-surface rebind follows. Silently dropping a zone
        // because its key was taken would lose a target the user still wants,
        // with no clue where it went.
        var layout = Fresh();
        var first = new GridPos(1, 0);
        var second = new GridPos(1, 2);

        var moved = LayoutEditor.RebindToKey(layout, first, KeyStroke.Plain(ScanF5)).Layout;
        var clash = LayoutEditor.RebindToKey(moved, second, KeyStroke.Plain(ScanF5));

        clash.Success.ShouldBeTrue();
        clash.Message.ShouldContain("Swapped");

        At(clash.Layout, second).KeyOn(clash.Layout.Surface).ShouldBe(KeyStroke.Plain(ScanF5));

        // The one that was displaced took the key the other gave up, rather than
        // ending up on the same key or on none.
        At(clash.Layout, first).KeyOn(clash.Layout.Surface)
            .ShouldNotBe(KeyStroke.Plain(ScanF5));
    }

    [Fact]
    public void AnArrowIsNotTheNumpadKeyItSharesAScanCodeWith()
    {
        // Left and Num4 are both 0x4B; only the extended prefix separates them.
        // Without it, binding a zone to Left would also fire it on Num4 - and on
        // the numpad surface that is another zone's key.
        var layout = Fresh();
        var home = new GridPos(1, 0);

        var arrow = new KeyStroke(0x4B, Extended: true);
        var numpad = KeyStroke.Plain(0x4B);

        arrow.ShouldNotBe(numpad);

        var bound = LayoutEditor.RebindToKey(layout, home, arrow).Layout;

        At(bound, home).KeyOn(bound.Surface).ShouldBe(arrow);
        At(bound, home).KeyOn(bound.Surface).ShouldNotBe(numpad);
    }

    [Theory]
    [InlineData(0x01)]   // Escape, which cancels a capture
    [InlineData(0x1D)]   // Ctrl
    [InlineData(0x2A)]   // Shift
    [InlineData(0x38)]   // Alt
    public void AKeyThatCannotBeAHotkeyIsRefused(int scan)
    {
        var layout = Fresh();

        var outcome = LayoutEditor.RebindToKey(
            layout, new GridPos(1, 0), KeyStroke.Plain((ushort)scan));

        outcome.Success.ShouldBeFalse();
        outcome.Layout.ShouldBeSameAs(layout);
    }

    [Fact]
    public void AKeyWithItsOwnModifierKeepsIt()
    {
        var layout = Fresh();
        var home = new GridPos(1, 0);
        var ctrlAlt = ChordModifiers.Control | ChordModifiers.Alt;

        var outcome = LayoutEditor.RebindToKey(
            layout, home, KeyStroke.Plain(ScanF5), ctrlAlt);

        At(outcome.Layout, home).Modifier.ShouldBe(ctrlAlt);
        At(outcome.Layout, home).KeyOn(outcome.Layout.Surface).ShouldBe(KeyStroke.Plain(ScanF5));
    }

    [Fact]
    public void AZoneLeftAloneStillFollowsTheSurface()
    {
        // The whole point of storing null: a zone nobody has moved must keep
        // taking its key from wherever it sits, so changing surface still works.
        var layout = Fresh();
        var home = new GridPos(1, 0);

        At(layout, home).Key.ShouldBeNull();
        At(layout, home).KeyOn(KeySurface.LeftHandBlock)
            .ShouldBe(KeyStroke.Plain(KeySurface.LeftHandBlock.ScanCodeAt(home)));
        At(layout, home).KeyOn(KeySurface.Numpad)
            .ShouldBe(KeyStroke.Plain(KeySurface.Numpad.ScanCodeAt(home)));
    }

    // ---- how a key is written down ----------------------------------------

    [Theory]
    [InlineData(ScanA, false)]
    [InlineData(ScanF5, false)]
    [InlineData(0x4B, true)]
    [InlineData(0x53, true)]
    public void AKeySurvivesTheRoundTripThroughAConfigFile(int scan, bool extended)
    {
        var key = new KeyStroke((ushort)scan, extended);

        KeyText.Read(KeyText.Write(key)).ShouldBe(key);
    }

    [Fact]
    public void TheExtendedPrefixIsWhatKeepsThemApartOnDisk()
    {
        KeyText.Write(KeyStroke.Plain(0x4B)).ShouldBe("4b");
        KeyText.Write(new KeyStroke(0x4B, Extended: true)).ShouldBe("e4b");

        KeyText.Read("4b").ShouldBe(KeyStroke.Plain(0x4B));
        KeyText.Read("e4b").ShouldBe(new KeyStroke(0x4B, Extended: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zz")]
    [InlineData("e")]
    [InlineData("0")]
    public void AnUnreadableKeyMeansUseTheDefault(string? text)
    {
        // One bad entry must not cost a whole profile, and it must never pin a
        // zone to a key nobody chose.
        KeyText.Read(text).ShouldBeNull();
    }

    [Fact]
    public void NothingWrittenForAZoneWithNoKeyOfItsOwn()
    {
        KeyText.Write(null).ShouldBeNull();
        KeyText.Write(KeyStroke.None).ShouldBeNull();
    }

    // ---- names -------------------------------------------------------------

    [Fact]
    public void AnArrowAndItsNumpadTwinAreNamedDifferently()
    {
        KeyNames.Of(new KeyStroke(0x4B, Extended: true)).ShouldBe("Left");
        KeyNames.Of(KeyStroke.Plain(0x4B)).ShouldBe("Num4");
    }

    [Fact]
    public void TheKeysPeopleWillReachForHaveNames()
    {
        KeyNames.Of(KeyStroke.Plain(ScanGrave)).ShouldBe("`");
        KeyNames.Of(KeyStroke.Plain(0x0E)).ShouldBe("Backspace");
        KeyNames.Of(KeyStroke.Plain(ScanF5)).ShouldBe("F5");
        KeyNames.Of(KeyStroke.Plain(0x39)).ShouldBe("Space");
        KeyNames.Of(new KeyStroke(0x53, Extended: true)).ShouldBe("Delete");
    }

    [Fact]
    public void AKeyNobodyKnowsStillReadsAsSomething()
    {
        // Better a name that says which key than a blank chip on the diagram.
        KeyNames.Of(KeyStroke.Plain(0xF0)).ShouldNotBeNullOrWhiteSpace();
        KeyNames.IsBindable(KeyStroke.Plain(0xF0)).ShouldBeFalse("we cannot name it, so we cannot offer it");
    }
}
