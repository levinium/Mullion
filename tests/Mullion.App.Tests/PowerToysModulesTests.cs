using System.Runtime.Versioning;
using Mullion.Platform.Windows.Windows;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Reading which PowerToys modules are switched on.
/// <para>
/// PowerToys ships as one install with two dozen modules, most of them off, and
/// it keeps each module's own configuration whatever that module's state. So the
/// presence of a Keyboard Manager remap file says nothing about whether Keyboard
/// Manager is running - and Mullion was warning about remaps belonging to a
/// feature the user had deliberately turned off. A warning that is wrong every
/// time you act on it is one you learn to ignore, including the time it is right.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class PowerToysModulesTests
{
    /// <summary>
    /// Trimmed from this machine's real settings.json, keeping the shape exactly:
    /// module names with spaces in them, and a mix of states.
    /// </summary>
    private const string Real =
        """
        {"startup":true,"enabled":{"AdvancedPaste":false,"AlwaysOnTop":true,"Awake":true,
        "FancyZones":false,"File Explorer":true,"Image Resizer":true,"Keyboard Manager":false,
        "Peek":true,"PowerRename":true,"Shortcut Guide":true},"show_tray_icon":true,
        "powertoys_version":"v0.100.2"}
        """;

    [Fact]
    public void ADisabledModuleReadsAsDisabled()
    {
        // The reported case, from the file as PowerToys actually writes it.
        PowerToysModules.Read(Real, PowerToysModules.KeyboardManager).ShouldBe(false);
    }

    [Fact]
    public void AnEnabledModuleReadsAsEnabled()
    {
        PowerToysModules.Read(Real, "Peek").ShouldBe(true);
    }

    [Fact]
    public void TheModuleNameKeepsItsSpaces()
    {
        // "Keyboard Manager", not "KeyboardManager". Getting this wrong reads as
        // "no idea" and warns anyway, which is the bug wearing a different hat.
        PowerToysModules.Read(Real, "KeyboardManager").ShouldBeNull();
        PowerToysModules.Read(Real, PowerToysModules.KeyboardManager).ShouldNotBeNull();
    }

    [Fact]
    public void FancyZonesIsReadTheSameWay()
    {
        PowerToysModules.Read(Real, PowerToysModules.FancyZones).ShouldBe(false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"enabled":null}""")]
    [InlineData("""{"enabled":"yes"}""")]
    [InlineData("""{"enabled":{"Keyboard Manager":"maybe"}}""")]
    public void AnythingUnreadableMeansDoNotKnow(string? json)
    {
        // Three answers, not two. This file belongs to another program and it may
        // be reshaped at any release; "cannot tell" has to be distinct from "off",
        // because only "off" is allowed to silence a warning.
        PowerToysModules.Read(json, PowerToysModules.KeyboardManager).ShouldBeNull();
    }

    [Fact]
    public void OnlyADefiniteOffCountsAsOff()
    {
        // What the warning actually asks. Not knowing must leave it speaking up.
        PowerToysModules.Read(Real, PowerToysModules.KeyboardManager).ShouldBe(false);
        PowerToysModules.Read(Real, "Workspaces").ShouldBeNull();
        PowerToysModules.Read(Real, "Peek").ShouldBe(true);
    }

    [Fact]
    public void AModuleMissingFromTheListIsNotAssumedOff()
    {
        // A newer PowerToys could rename or drop a key. Falling silent then would
        // hide a real conflict; staying noisy is the safe direction to fail.
        PowerToysModules.Read("""{"enabled":{"Peek":true}}""", PowerToysModules.KeyboardManager)
            .ShouldBeNull();
    }
}
