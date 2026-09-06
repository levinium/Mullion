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

    private static HotkeyStateMachine Machine(
        WinKeySuppression suppression = WinKeySuppression.DummyKey,
        IReadOnlyList<HotkeyAction>? ringA = null)
    {
        var sm = new HotkeyStateMachine(suppression);

        var bindings = new Dictionary<(ChordModifiers, ushort), HotkeyAction>
        {
            [(ChordModifiers.Win, ScanA)] = new("zone-a"),
            [(ChordModifiers.Win, ScanS)] = new("zone-s"),
            [(ChordModifiers.Win, ScanD)] = new("zone-d"),
        };

        var rings = new Dictionary<(ChordModifiers, ushort), IReadOnlyList<HotkeyAction>>();
        if (ringA is not null) rings[(ChordModifiers.Win, ScanA)] = ringA;

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
        ushort scan = 0;

        sm.ChordCaptured += (m, s) => { mods = m; scan = s; };
        sm.BeginCapture();

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(0, VkLShift));
        sm.Process(Down(ScanS, VkS)).Action.ShouldBe(HookAction.Swallow);

        mods.ShouldBe(ChordModifiers.Win | ChordModifiers.Shift);
        scan.ShouldBe(ScanS);
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
}
