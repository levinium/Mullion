using Mullion.Core.Hotkeys;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class ModifierChoiceTests
{
    [Theory]
    [InlineData("Win", ChordModifiers.Win)]
    [InlineData("Ctrl+Alt", ChordModifiers.Control | ChordModifiers.Alt)]
    [InlineData("Alt+Shift", ChordModifiers.Alt | ChordModifiers.Shift)]
    [InlineData("Win+Shift", ChordModifiers.Win | ChordModifiers.Shift)]
    [InlineData("control shift", ChordModifiers.Control | ChordModifiers.Shift)]
    public void ParsesTheNamesOffered(string name, ChordModifiers expected) =>
        ModifierChoice.Parse(name).ShouldBe(expected);

    /// <summary>
    /// The one case that must not do the obvious thing. Falling back to no
    /// modifier would bind every zone to a bare letter, so a typo in the config
    /// would fire hotkeys while typing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Hyper")]
    [InlineData("!!")]
    public void FallsBackToWinRatherThanNoModifier(string? name) =>
        ModifierChoice.Parse(name).ShouldBe(ChordModifiers.Win);

    [Fact]
    public void EveryOfferedNameRoundTrips()
    {
        foreach (var name in ModifierChoice.All)
            ModifierChoice.Format(ModifierChoice.Parse(name)).ShouldBe(name);
    }

    [Fact]
    public void OnlyTheWindowsKeyNeedsSuppressing()
    {
        ModifierChoice.NeedsWinKeySuppression(ChordModifiers.Win).ShouldBeTrue();
        ModifierChoice.NeedsWinKeySuppression(ChordModifiers.Win | ChordModifiers.Shift).ShouldBeTrue();
        ModifierChoice.NeedsWinKeySuppression(ChordModifiers.Control | ChordModifiers.Alt).ShouldBeFalse();
    }
}
