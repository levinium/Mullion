using Mullion.Core.Hotkeys;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// The hotkeys that are not zones, and how a stored change is laid over them.
/// <para>
/// These were a two-entry array inside the engine, bound to the default modifier
/// and unchangeable. Defensible with one of them; not with several, and never
/// defensible that the single hotkey nobody could rebind was the one on
/// Backspace.
/// </para>
/// </summary>
public class GlobalActionTests
{
    private static readonly KeyStroke Backspace = KeyStroke.Plain(0x0E);
    private static readonly KeyStroke Grave = KeyStroke.Plain(0x29);
    private static readonly KeyStroke F9 = KeyStroke.Plain(0x43);

    [Fact]
    public void WhatMullionShipsWith()
    {
        var defaults = GlobalAction.Defaults;

        defaults.Single(a => a.Command == GlobalAction.Undo).Key.ShouldBe(Backspace);
        defaults.Single(a => a.Command == GlobalAction.Minimize).Key.ShouldBe(Grave);
    }

    [Fact]
    public void BothSitOutsideTheKeySurface()
    {
        // Not a stylistic choice. The allocator may claim any surface key when a
        // monitor is plugged in, so an action bound there would stop working on
        // the day the desk changed - silently, and long after anyone would connect
        // the two events.
        foreach (var action in GlobalAction.Defaults)
        {
            foreach (var surface in KeySurface.All)
            {
                surface.ScanCodes.ShouldNotContain(action.Key.ScanCode,
                    $"{action.Command} is on a key the {surface.Name} surface uses");
            }
        }
    }

    [Fact]
    public void NothingStoredMeansTheDefaults()
    {
        GlobalAction.Resolve(null).ShouldBe(GlobalAction.Defaults);
        GlobalAction.Resolve([]).ShouldBe(GlobalAction.Defaults);
    }

    [Fact]
    public void AStoredChangeReplacesJustThatOne()
    {
        var resolved = GlobalAction.Resolve([(GlobalAction.Minimize, F9, null)]);

        resolved.Single(a => a.Command == GlobalAction.Minimize).Key.ShouldBe(F9);
        resolved.Single(a => a.Command == GlobalAction.Undo).Key.ShouldBe(Backspace);
    }

    [Fact]
    public void AnActionAddedLaterReachesPeopleWhoHaveAlreadySavedSettings()
    {
        // The reason this layers rather than replaces. Storing the whole list
        // would mean a new action only ever appeared for people who had never
        // opened the settings screen.
        var resolved = GlobalAction.Resolve([(GlobalAction.Undo, F9, null)]);

        resolved.Count.ShouldBe(GlobalAction.Defaults.Count);
        resolved.ShouldContain(a => a.Command == GlobalAction.Minimize);
    }

    [Fact]
    public void AStoredChordComesAcrossToo()
    {
        var ctrlAlt = ChordModifiers.Control | ChordModifiers.Alt;

        var resolved = GlobalAction.Resolve([(GlobalAction.Undo, F9, ctrlAlt)]);
        var undo = resolved.Single(a => a.Command == GlobalAction.Undo);

        undo.Modifier.ShouldBe(ctrlAlt);
        undo.ChordWith(ChordModifiers.Win).ShouldBe(ctrlAlt);
    }

    [Fact]
    public void AnActionWithNoChordOfItsOwnFollowsTheDefault()
    {
        // Same rule zones follow, so changing the default modifier moves
        // everything nobody has pinned.
        GlobalAction.Defaults[0].ChordWith(ChordModifiers.Win).ShouldBe(ChordModifiers.Win);
        GlobalAction.Defaults[0].ChordWith(ChordModifiers.Alt).ShouldBe(ChordModifiers.Alt);
    }

    [Fact]
    public void AStoredEntryWithNoKeyIsIgnored()
    {
        // Rather than binding the action to nothing, which would silently remove
        // a hotkey that still appears in the settings list.
        GlobalAction.Resolve([(GlobalAction.Undo, null, null)]).ShouldBe(GlobalAction.Defaults);
        GlobalAction.Resolve([(GlobalAction.Undo, KeyStroke.None, null)]).ShouldBe(GlobalAction.Defaults);
    }

    [Fact]
    public void AnEntryForSomethingThatIsNotAnActionIsIgnored()
    {
        // A command from a future version, or a hand-edited file.
        GlobalAction.Resolve([("teleport", F9, null)]).ShouldBe(GlobalAction.Defaults);
    }

    [Fact]
    public void EachActionHasSomethingToCallIt()
    {
        foreach (var action in GlobalAction.Defaults)
        {
            action.Title.ShouldNotBeNullOrWhiteSpace();
            action.Title.ShouldNotBe(action.Command, "the title is for people, not for the config file");
        }
    }
}
