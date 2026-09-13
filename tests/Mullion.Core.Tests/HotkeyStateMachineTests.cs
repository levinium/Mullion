using Mullion.Core.Hotkeys;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

public class HotkeyStateMachineTests
{
    private const ushort VkLWin = 0x5B;
    private const ushort VkLShift = 0xA0;
    private const ushort ScanA = 0x1E;
    private const ushort ScanS = 0x1F;
    private const ushort ScanD = 0x20;
    private const ushort VkA = 0x41;
    private const ushort VkS = 0x53;
    private const ushort VkD = 0x44;

    private static KeyEvent Down(ushort scan, ushort vk) => new(scan, vk, false, false, false, 0);
    private static KeyEvent Up(ushort scan, ushort vk) => new(scan, vk, true, false, false, 0);

    /// <param name="flipA">
    /// What Win+Shift+A does - the subzone flip the engine binds on the same key.
    /// Present so the tests can exercise two chords sharing one physical key,
    /// which is where the cycle's identity stops being just the key.
    /// </param>
    private static HotkeyStateMachine Machine(
        WinKeySuppression suppression = WinKeySuppression.DummyKey,
        IReadOnlyList<HotkeyAction>? ringA = null,
        HotkeyAction? flipA = null)
    {
        var sm = new HotkeyStateMachine(suppression);

        var bindings = new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>
        {
            [(ChordModifiers.Win, KeyStroke.Plain(ScanA))] = new("zone-a"),
            [(ChordModifiers.Win, KeyStroke.Plain(ScanS))] = new("zone-s"),
            [(ChordModifiers.Win, KeyStroke.Plain(ScanD))] = new("zone-d"),
        };

        if (flipA is not null)
            bindings[(ChordModifiers.Win | ChordModifiers.Shift, KeyStroke.Plain(ScanA))] = flipA;

        var rings = new Dictionary<(ChordModifiers, KeyStroke), IReadOnlyList<HotkeyAction>>();
        if (ringA is not null) rings[(ChordModifiers.Win, KeyStroke.Plain(ScanA))] = ringA;

        sm.SetBindings(bindings, rings);
        return sm;
    }

    [Fact]
    public void WinPlusASwallowsAndFiresWithADummyKey()
    {
        var sm = Machine();

        sm.Process(Down(0, VkLWin)).Action.ShouldBe(HookAction.PassThrough);

        var hit = sm.Process(Down(ScanA, VkA));
        hit.Fire!.Id.ShouldBe("zone-a");
        hit.Action.HasFlag(HookAction.Swallow).ShouldBeTrue();
        hit.Action.HasFlag(HookAction.InjectDummyKey).ShouldBeTrue("Start menu must be suppressed");

        // The key-up must be swallowed too, or an orphan reaches the focused app.
        sm.Process(Up(ScanA, VkA)).Action.ShouldBe(HookAction.Swallow);

        // With the dummy key already injected the Win key-up passes through.
        sm.Process(Up(0, VkLWin)).Action.ShouldBe(HookAction.PassThrough);
    }

    [Fact]
    public void SwallowKeyUpModeSwallowsTheWinRelease()
    {
        var sm = Machine(WinKeySuppression.SwallowKeyUp);

        sm.Process(Down(0, VkLWin));
        var hit = sm.Process(Down(ScanA, VkA));
        hit.Action.HasFlag(HookAction.InjectDummyKey).ShouldBeFalse();

        sm.Process(Up(ScanA, VkA));
        sm.Process(Up(0, VkLWin)).Action.ShouldBe(HookAction.Swallow);
    }

    [Fact]
    public void UnmatchedKeysPassThroughSoTypingIsUnaffected()
    {
        var sm = Machine();

        // A plain 'a' with no modifier must reach the application.
        sm.Process(Down(ScanA, VkA)).Action.ShouldBe(HookAction.PassThrough);
        sm.Process(Up(ScanA, VkA)).Action.ShouldBe(HookAction.PassThrough);
    }

    /// <summary>Win+Shift+A must not fire the Win+A binding.</summary>
    [Fact]
    public void ModifierMatchingIsStrict()
    {
        var sm = Machine();

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(0, VkLShift));

        var decision = sm.Process(Down(ScanA, VkA));
        decision.Fire.ShouldBeNull();
        decision.Action.ShouldBe(HookAction.PassThrough);
    }

    // ---- cycling, gated on the modifier being held -------------------------

    [Fact]
    public void RepeatPressAdvancesTheRingWhileWinIsHeld()
    {
        var ring = new HotkeyAction[] { new("a-third"), new("a-half"), new("a-full") };
        var sm = Machine(ringA: ring);

        sm.Process(Down(0, VkLWin));

        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-half");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-full");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third", "the ring must wrap");
    }

    [Fact]
    public void ReleasingWinResetsTheCycle()
    {
        var ring = new HotkeyAction[] { new("a-third"), new("a-half") };
        var sm = Machine(ringA: ring);

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-half");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Up(0, VkLWin));

        // Fresh press of Win starts the ring over.
        sm.Process(Down(0, VkLWin));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third");
    }

    [Fact]
    public void DroppingShiftAndPressingAgainStartsThatKeysOwnRing()
    {
        // Win+Shift+A and Win+A are different hotkeys that happen to share a key.
        // Letting go of Shift and pressing A is therefore a first press of Win+A,
        // and it must give that zone - not the second entry of its ring.
        //
        // Tracking the scan code alone, the machine could not tell this from
        // holding A down: it had already "shown" A, so it moved on, and the size
        // the press was actually asking for was skipped.
        var ring = new HotkeyAction[] { new("a-third"), new("a-half") };
        var sm = Machine(ringA: ring, flipA: new HotkeyAction("a-flipped"));

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(VkLShift, VkLShift));

        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-flipped");
        sm.Process(Up(ScanA, VkA));

        // Shift goes; Win stays down.
        sm.Process(Up(VkLShift, VkLShift));

        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third",
            "the plain chord has not been seen yet, so it starts at its first size");
    }

    [Fact]
    public void TheRingStillAdvancesAfterThatFirstPlainPress()
    {
        // The other half of the pair: resetting on a chord change must not leave
        // the ring stuck at its first entry for the rest of the Win press.
        var ring = new HotkeyAction[] { new("a-third"), new("a-half") };
        var sm = Machine(ringA: ring, flipA: new HotkeyAction("a-flipped"));

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(VkLShift, VkLShift));
        sm.Process(Down(ScanA, VkA));
        sm.Process(Up(ScanA, VkA));
        sm.Process(Up(VkLShift, VkLShift));

        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-half");
    }

    [Fact]
    public void TakingShiftBackUpStartsTheFlipAgainToo()
    {
        // Symmetric: the flipped chord is equally entitled to its own first
        // press, so going back to it must not inherit the plain ring's position.
        var ring = new HotkeyAction[] { new("a-third"), new("a-half") };
        var sm = Machine(ringA: ring, flipA: new HotkeyAction("a-flipped"));

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-half");
        sm.Process(Up(ScanA, VkA));

        sm.Process(Down(VkLShift, VkLShift));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-flipped");
    }

    [Fact]
    public void PressingADifferentAnchorResetsTheCycle()
    {
        var ring = new HotkeyAction[] { new("a-third"), new("a-half") };
        var sm = Machine(ringA: ring);

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-half");
        sm.Process(Up(ScanA, VkA));

        // Different key while Win is still held.
        sm.Process(Down(ScanD, VkD)).Fire!.Id.ShouldBe("zone-d");
        sm.Process(Up(ScanD, VkD));

        // Back to A: the ring must have restarted.
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("a-third");
    }

    // ---- robustness --------------------------------------------------------

    [Fact]
    public void AutoRepeatIsSwallowedAndDoesNotStreamCharacters()
    {
        var sm = Machine();

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(ScanA, VkA));

        // Held key: Windows streams repeats. Every one must be swallowed.
        for (var i = 0; i < 5; i++)
            sm.Process(Down(ScanA, VkA)).Action.HasFlag(HookAction.Swallow).ShouldBeTrue();
    }

    [Fact]
    public void OurOwnInjectedEventsAreIgnored()
    {
        var sm = Machine();

        var injected = new KeyEvent(ScanA, VkA, false, true, true, 0);
        sm.Process(injected).ShouldBe(HookDecision.Pass);
    }

    [Fact]
    public void ThirdPartyInjectionCanBeIgnoredWhenConfigured()
    {
        var sm = Machine();
        sm.IgnoreInjected = true;

        sm.Process(Down(0, VkLWin));
        var fromAutoHotkey = new KeyEvent(ScanA, VkA, false, true, false, 0);
        sm.Process(fromAutoHotkey).Action.ShouldBe(HookAction.PassThrough);
    }

    [Fact]
    public void PausedMachinePassesEverythingThrough()
    {
        var sm = Machine();
        sm.Enabled = false;

        sm.Process(Down(0, VkLWin)).Action.ShouldBe(HookAction.PassThrough);
        sm.Process(Down(ScanA, VkA)).Action.ShouldBe(HookAction.PassThrough);
    }

    /// <summary>
    /// A missed key-up (session lock, RDP, sleep) otherwise leaves Win stuck on
    /// forever, silently swallowing every subsequent A/S/D.
    /// </summary>
    [Fact]
    public void ResyncClearsAStuckModifier()
    {
        var sm = Machine();

        sm.Process(Down(0, VkLWin));
        sm.CurrentModifiers.ShouldBe(ChordModifiers.Win);

        sm.ResyncModifiers(ChordModifiers.None);

        sm.CurrentModifiers.ShouldBe(ChordModifiers.None);
        sm.Process(Down(ScanA, VkA)).Action.ShouldBe(HookAction.PassThrough);
    }

    [Fact]
    public void CaptureModeRecordsAChordTheUiCouldNeverSee()
    {
        var sm = Machine();
        ChordModifiers? mods = null;
        var key = KeyStroke.None;

        sm.ChordCaptured += (m, k) => { mods = m; key = k; };
        sm.BeginCapture();

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(0, VkLShift));
        sm.Process(Down(ScanS, VkS)).Action.ShouldBe(HookAction.Swallow);

        mods.ShouldBe(ChordModifiers.Win | ChordModifiers.Shift);
        key.ShouldBe(KeyStroke.Plain(ScanS));
    }

    [Fact]
    public void EscapeCancelsCapture()
    {
        var sm = Machine();
        var fired = false;
        sm.ChordCaptured += (_, _) => fired = true;

        sm.BeginCapture();
        sm.Process(Down(0x01, 0x1B)).Action.ShouldBe(HookAction.Swallow);

        fired.ShouldBeFalse();
        sm.Process(Down(ScanA, VkA)).Action.ShouldBe(HookAction.PassThrough);
    }

    /// <summary>
    /// Bindings are keyed by scan code, so the physical grid survives a layout
    /// change. On AZERTY the same physical key emits Q, not A.
    /// </summary>
    [Fact]
    public void MatchingUsesScanCodeNotVirtualKey()
    {
        var sm = Machine();
        sm.Process(Down(0, VkLWin));

        const ushort VkQ = 0x51;   // what AZERTY reports for the same physical key
        sm.Process(new KeyEvent(ScanA, VkQ, false, false, false, 0)).Fire!.Id.ShouldBe("zone-a");
    }

    [Fact]
    public void EscapeDuringCaptureCancelsAndSaysSo()
    {
        // The machine already stopped capturing on Escape, but silently - so the
        // hook swallowed the key, the capture ended, and the UI went on saying
        // it was waiting for one. Nothing above could find out on its own,
        // because a capture is armed and the hook sees the key first.
        var machine = Machine();

        var captured = 0;
        var canceled = 0;

        machine.ChordCaptured += (_, _) => captured++;
        machine.CaptureCanceled += () => canceled++;

        machine.BeginCapture();

        var decision = machine.Process(new KeyEvent(0x01, 0x1B, false, false, false, 0));

        canceled.ShouldBe(1);
        captured.ShouldBe(0, "Escape is not a chord to bind");
        decision.Action.ShouldBe(HookAction.Swallow, "the key must not reach whatever has focus");
    }

    [Fact]
    public void EscapeAfterCaptureIsLeftAlone()
    {
        // Outside a capture, Escape is just a key and belongs to whatever has
        // focus - swallowing it would break every dialog on the machine.
        var machine = Machine();

        var canceled = 0;
        machine.CaptureCanceled += () => canceled++;

        var decision = machine.Process(new KeyEvent(0x01, 0x1B, false, false, false, 0));

        canceled.ShouldBe(0);
        decision.Action.ShouldBe(HookAction.PassThrough);
    }
}
