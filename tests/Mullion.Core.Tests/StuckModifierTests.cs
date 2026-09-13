using Mullion.Core.Hotkeys;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// Recovering from a modifier key-up that never arrived.
/// <para>
/// Modifier tracking is built from the events the hook is handed, which assumes
/// every key-down is followed by its key-up. Windows breaks that whenever the
/// desktop changes underneath a held key: Win+L hands the lock screen the key-up,
/// a UAC prompt takes it to the secure desktop, and the fast-user-switch does the
/// same. Win is then held forever as far as this machine knows, and the next bare
/// Q moves a window. From the outside that reads as the app inventing hotkey
/// presses after signing in.
/// </para>
/// <para>
/// The recovery was written - ResyncModifiers has been here all along, with a
/// comment naming these exact cases - and never called by anything.
/// </para>
/// </summary>
public class StuckModifierTests
{
    private const ushort VkLWin = 0x5B;
    private const ushort VkLShift = 0xA0;
    private const ushort ScanA = 0x1E;
    private const ushort VkA = 0x41;

    private static KeyEvent Down(ushort scan, ushort vk) => new(scan, vk, false, false, false, 0);
    private static KeyEvent Up(ushort scan, ushort vk) => new(scan, vk, true, false, false, 0);

    /// <summary>No cycling in any of this; the question is only which chord fired.</summary>
    private static readonly Dictionary<(ChordModifiers, KeyStroke), IReadOnlyList<HotkeyAction>> NoRings = [];

    [Fact]
    public void AWinKeyUpThatNeverArrivesStopsFiringHotkeys()
    {
        // The reported bug, in full. Win goes down, the lock screen swallows the
        // key-up, and afterwards a plain A must be a plain A.
        var sm = new HotkeyStateMachine(WinKeySuppression.DummyKey);
        sm.SetBindings(
            new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>
            {
                [(ChordModifiers.Win, KeyStroke.Plain(ScanA))] = new("zone-a"),
            },
            NoRings);

        var physical = ChordModifiers.Win;
        sm.PhysicalModifiers = () => physical;

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("zone-a");
        sm.Process(Up(ScanA, VkA));

        // The screen locks. Windows delivers the Win key-up somewhere else, so
        // nothing arrives here - but the key is no longer physically held.
        physical = ChordModifiers.None;

        var after = sm.Process(Down(ScanA, VkA));

        after.Fire.ShouldBeNull("a bare A must not move a window");
        after.Action.ShouldBe(HookAction.PassThrough, "and it must reach whatever is focused");
    }

    [Fact]
    public void AModifierThatIsGenuinelyHeldIsLeftAlone()
    {
        // The other side of it: this must not become a machine that forgets a
        // key you are actually holding, which would break every hotkey.
        var sm = new HotkeyStateMachine(WinKeySuppression.DummyKey);
        sm.SetBindings(
            new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>
            {
                [(ChordModifiers.Win, KeyStroke.Plain(ScanA))] = new("zone-a"),
            },
            NoRings);

        sm.PhysicalModifiers = () => ChordModifiers.Win;

        sm.Process(Down(0, VkLWin));

        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("zone-a");
        sm.Process(Up(ScanA, VkA));
        sm.Process(Down(ScanA, VkA)).Fire.ShouldNotBeNull("still held, so still firing");
    }

    [Fact]
    public void OnlyTheStuckModifierIsDropped()
    {
        // Shift really is down and Win really is not. Clearing both would be a
        // different bug wearing the same fix.
        var sm = new HotkeyStateMachine(WinKeySuppression.DummyKey);
        sm.SetBindings(
            new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>
            {
                [(ChordModifiers.Shift, KeyStroke.Plain(ScanA))] = new("shift-a"),
            },
            NoRings);

        var physical = ChordModifiers.Win | ChordModifiers.Shift;
        sm.PhysicalModifiers = () => physical;

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(VkLShift, VkLShift));

        physical = ChordModifiers.Shift;

        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("shift-a");
    }

    [Fact]
    public void ThePhysicalStateIsNotConsultedWhileNothingIsHeld()
    {
        // Ordinary typing is the hot path, and it must not pay for this. With no
        // modifier believed down there is nothing that could be stuck, so the
        // keyboard is never read at all.
        var sm = new HotkeyStateMachine(WinKeySuppression.DummyKey);
        sm.SetBindings(new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>(), NoRings);

        var reads = 0;
        sm.PhysicalModifiers = () => { reads++; return ChordModifiers.None; };

        for (var i = 0; i < 50; i++)
        {
            sm.Process(Down(ScanA, VkA));
            sm.Process(Up(ScanA, VkA));
        }

        reads.ShouldBe(0);
    }

    [Fact]
    public void AKeyGoingDownIsNotMistakenForAStuckOne()
    {
        // The modifier being pressed right now is physically down before this
        // machine has been told. Adding it here would race the event that is
        // about to do it properly, so the check only ever drops.
        var sm = new HotkeyStateMachine(WinKeySuppression.DummyKey);
        sm.SetBindings(
            new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>
            {
                [(ChordModifiers.Win | ChordModifiers.Shift, KeyStroke.Plain(ScanA))] = new("win-shift-a"),
            },
            NoRings);

        sm.PhysicalModifiers = () => ChordModifiers.Win | ChordModifiers.Shift;

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(VkLShift, VkLShift));

        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("win-shift-a");
    }

    [Fact]
    public void WithNoWayToReadTheKeyboardNothingChanges()
    {
        // Core has no business calling Win32, so the reader is injected - and a
        // machine without one has to behave exactly as it always did.
        var sm = new HotkeyStateMachine(WinKeySuppression.DummyKey);
        sm.SetBindings(
            new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>
            {
                [(ChordModifiers.Win, KeyStroke.Plain(ScanA))] = new("zone-a"),
            },
            NoRings);

        sm.PhysicalModifiers = null;

        sm.Process(Down(0, VkLWin));
        sm.Process(Down(ScanA, VkA)).Fire!.Id.ShouldBe("zone-a");
    }
}
