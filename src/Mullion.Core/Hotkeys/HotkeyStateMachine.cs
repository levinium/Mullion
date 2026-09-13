namespace Mullion.Core.Hotkeys;

[Flags]
public enum ChordModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A key event as seen by the hook, reduced to what matching needs.</summary>
public readonly record struct KeyEvent(
    ushort ScanCode,
    ushort VirtualKey,
    bool IsKeyUp,
    bool IsInjected,
    bool IsOurInjection,
    long TimestampMs,
    bool IsExtended = false)
{
    /// <summary>The key this event is about, as a binding identifies it.</summary>
    public KeyStroke Key => new(ScanCode, IsExtended);
}

[Flags]
public enum HookAction
{
    PassThrough = 0,

    /// <summary>Do not forward to the rest of the chain.</summary>
    Swallow = 1,

    /// <summary>
    /// Inject a benign keypress so the shell records the Win key as "used" and
    /// does not open the Start menu when it is released.
    /// </summary>
    InjectDummyKey = 2,
}

/// <summary>What a matched chord should do.</summary>
public sealed record HotkeyAction(string Id, GridPos? Zone = null, string? Command = null);

public readonly record struct HookDecision(HookAction Action, HotkeyAction? Fire = null)
{
    public static readonly HookDecision Pass = new(HookAction.PassThrough);
}

public enum WinKeySuppression
{
    /// <summary>Let the Start menu open. Only useful for diagnosis.</summary>
    None,

    /// <summary>Inject VK 0xFF so the shell sees the Win press as modified.</summary>
    DummyKey,

    /// <summary>Swallow the Win key-up entirely.</summary>
    SwallowKeyUp,
}

/// <summary>
/// Pure chord matching, with no Windows dependency at all.
/// <para>
/// Keeping this separate from the hook is what makes the hardest part of the
/// app testable: cycling, Start-menu suppression, auto-repeat and stuck-modifier
/// recovery can all be driven from scripted key events with no hardware and no
/// timing. The hook itself becomes a thin adapter.
/// </para>
/// <para>
/// Cycling advances only while the modifier is HELD and resets on release or on
/// a different anchor being pressed. That is deterministic - the hook observes
/// the real key-up - where a timeout would race with fast typing.
/// </para>
/// </summary>
public sealed class HotkeyStateMachine(
    WinKeySuppression suppression = WinKeySuppression.DummyKey,
    bool ignoreInjected = false)
{
    // Virtual-key codes for modifiers. Tracked per physical key so that
    // releasing one Shift while the other is held does not clear the flag.
    private const ushort VkLShift = 0xA0, VkRShift = 0xA1;
    private const ushort VkLControl = 0xA2, VkRControl = 0xA3;
    private const ushort VkLMenu = 0xA4, VkRMenu = 0xA5;
    private const ushort VkLWin = 0x5B, VkRWin = 0x5C;
    private const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    private const ushort VkEscape = 0x1B;

    private readonly HashSet<ushort> _modifiersDown = [];

    private Dictionary<(ChordModifiers Mods, KeyStroke Key), HotkeyAction> _bindings = [];
    private Dictionary<(ChordModifiers Mods, KeyStroke Key), IReadOnlyList<HotkeyAction>> _rings = [];

    private KeyStroke _activeKey;

    /// <summary>
    /// The chord the active cycle belongs to, not merely the key.
    /// <para>
    /// Both halves are needed. Win+Shift+Q and Win+Q are different hotkeys landing
    /// on the same physical key, so with the scan code alone the second looked
    /// exactly like a repeat of the first and advanced the ring - releasing Shift
    /// and pressing Q again skipped the size that press was asking for, because
    /// the machine believed it had already been shown.
    /// </para>
    /// </summary>
    private ChordModifiers _activeMods;

    private int _cycleIndex;
    private bool _winConsumed;
    private bool _capturing;

    public bool Enabled { get; set; } = true;

    public WinKeySuppression Suppression { get; set; } = suppression;

    public bool IgnoreInjected { get; set; } = ignoreInjected;

    public event Action<ChordModifiers, KeyStroke>? ChordCaptured;

    /// <summary>
    /// Escape ended the capture without a chord.
    /// <para>
    /// Announced rather than handled silently: the key has to be swallowed
    /// here, because a capture is armed and the hook sees it first, so nothing
    /// above finds out on its own. Without this the machine stopped listening
    /// while the UI went on saying it was waiting for a key.
    /// </para>
    /// </summary>
    public event Action? CaptureCanceled;

    public void SetBindings(
        IReadOnlyDictionary<(ChordModifiers, KeyStroke), HotkeyAction> bindings,
        IReadOnlyDictionary<(ChordModifiers, KeyStroke), IReadOnlyList<HotkeyAction>>? rings = null)
    {
        _bindings = bindings.ToDictionary(kv => kv.Key, kv => kv.Value);
        _rings = rings?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];
        Reset();
    }

    public void BeginCapture() { _capturing = true; }

    public void EndCapture() { _capturing = false; }

    /// <summary>
    /// Let go of any modifier the keyboard says is not actually held.
    /// <para>
    /// Only ever drops. A modifier we believe is down and physically is not can
    /// only cause harm - it fires hotkeys nobody asked for and swallows the keys
    /// they were typing. The opposite mismatch is ordinary and momentary: the
    /// key-down being processed right now is physically down before this machine
    /// has been told about it, and adding it here would be racing the very event
    /// that is about to do it properly.
    /// </para>
    /// <para>
    /// Checked on every event, but only paid for when something is believed held:
    /// with nothing down - which is the whole of ordinary typing - this reads one
    /// field and returns.
    /// </para>
    /// </summary>
    private void DropStuckModifiers()
    {
        if (PhysicalModifiers is null || _modifiersDown.Count == 0) return;

        var believed = CurrentModifiers;
        if (believed == ChordModifiers.None) return;

        var physical = PhysicalModifiers();
        if ((believed & ~physical) == 0) return;

        ResyncModifiers(physical);
    }

    /// <summary>
    /// Re-read which modifiers are physically down. Called after a re-hook, a
    /// session unlock or a resume, where key-ups can be missed entirely and
    /// leave a modifier stuck on forever.
    /// </summary>
    public void ResyncModifiers(ChordModifiers actuallyDown)
    {
        _modifiersDown.Clear();

        if (actuallyDown.HasFlag(ChordModifiers.Shift)) _modifiersDown.Add(VkLShift);
        if (actuallyDown.HasFlag(ChordModifiers.Control)) _modifiersDown.Add(VkLControl);
        if (actuallyDown.HasFlag(ChordModifiers.Alt)) _modifiersDown.Add(VkLMenu);
        if (actuallyDown.HasFlag(ChordModifiers.Win)) _modifiersDown.Add(VkLWin);

        _activeKey = KeyStroke.None;
        _activeMods = ChordModifiers.None;
        _cycleIndex = 0;
        _winConsumed = false;
    }

    public void Reset()
    {
        _modifiersDown.Clear();
        _activeKey = KeyStroke.None;
        _activeMods = ChordModifiers.None;
        _cycleIndex = 0;
        _winConsumed = false;
    }

    public ChordModifiers CurrentModifiers
    {
        get
        {
            var mods = ChordModifiers.None;
            if (_modifiersDown.Contains(VkLShift) || _modifiersDown.Contains(VkRShift) || _modifiersDown.Contains(VkShift))
                mods |= ChordModifiers.Shift;
            if (_modifiersDown.Contains(VkLControl) || _modifiersDown.Contains(VkRControl) || _modifiersDown.Contains(VkControl))
                mods |= ChordModifiers.Control;
            if (_modifiersDown.Contains(VkLMenu) || _modifiersDown.Contains(VkRMenu) || _modifiersDown.Contains(VkMenu))
                mods |= ChordModifiers.Alt;
            if (_modifiersDown.Contains(VkLWin) || _modifiersDown.Contains(VkRWin))
                mods |= ChordModifiers.Win;
            return mods;
        }
    }

    /// <summary>
    /// How to read the keyboard's ACTUAL modifier state, set by the platform.
    /// <para>
    /// Modifier tracking is built from the key events the hook is handed, which
    /// assumes every key-down is eventually followed by its key-up. Windows
    /// breaks that assumption whenever the desktop changes underneath a held key:
    /// press Win+L, or let a UAC prompt take the secure desktop, and the key-up
    /// is delivered somewhere this hook cannot see. The modifier is then held
    /// forever as far as Mullion is concerned, and the next bare Q moves a
    /// window - which is exactly what it looks like from the outside, a machine
    /// that starts firing hotkeys nobody pressed after signing in.
    /// </para>
    /// <para>
    /// A delegate rather than a call into the platform, so Core stays free of
    /// Win32 and the recovery can be tested without a keyboard.
    /// </para>
    /// </summary>
    public Func<ChordModifiers>? PhysicalModifiers { get; set; }

    public HookDecision Process(in KeyEvent e)
    {
        // 1. Our own injected events must never be reprocessed, or the dummy key
        //    feeds back into the machine. Checked before the enabled flag so the
        //    guard holds even while paused.
        if (e.IsOurInjection) return HookDecision.Pass;
        if (e.IsInjected && IgnoreInjected) return HookDecision.Pass;

        DropStuckModifiers();

        // 2. Paused: cheapest possible path back out.
        if (!Enabled && !_capturing) return HookDecision.Pass;

        // 3. Modifier tracking.
        if (IsModifier(e.VirtualKey))
        {
            if (e.IsKeyUp)
            {
                _modifiersDown.Remove(e.VirtualKey);

                if (IsWin(e.VirtualKey))
                {
                    // Cycling resets when the modifier is released - the whole
                    // point of gating on hold rather than on a timer.
                    _activeKey = KeyStroke.None;
                    _activeMods = ChordModifiers.None;
                    _cycleIndex = 0;

                    if (_winConsumed)
                    {
                        _winConsumed = false;
                        if (Suppression == WinKeySuppression.SwallowKeyUp)
                            return new HookDecision(HookAction.Swallow);
                    }
                }
            }
            else
            {
                _modifiersDown.Add(e.VirtualKey);
            }

            return HookDecision.Pass;
        }

        // 4. Capture mode for the "press a key" control. This is the only way to
        //    record Win+A at all, since the UI framework never receives it.
        if (_capturing)
        {
            if (!e.IsKeyUp)
            {
                if (e.VirtualKey == VkEscape)
                {
                    _capturing = false;
                    CaptureCanceled?.Invoke();
                    return new HookDecision(HookAction.Swallow);
                }
                ChordCaptured?.Invoke(CurrentModifiers, e.Key);
            }

            return new HookDecision(HookAction.Swallow);
        }

        var mods = CurrentModifiers;
        var key = (mods, e.Key);

        // 5. Key-up of the key we consumed: swallow so no orphan reaches the app.
        if (e.IsKeyUp)
        {
            if (e.Key == _activeKey) return new HookDecision(HookAction.Swallow);
            return HookDecision.Pass;
        }

        // 6. Auto-repeat. Repeats must be swallowed as well as the original, or
        //    holding the key streams characters into the focused app.
        if (e.Key == _activeKey && mods == _activeMods && _bindings.ContainsKey(key))
        {
            var repeated = Advance(key);
            return new HookDecision(SwallowWith(mods), repeated);
        }

        // 7. Match. Strict modifier equality, so Win+Shift+A never fires Win+A.
        if (!_bindings.TryGetValue(key, out var action)) return HookDecision.Pass;

        if (_activeKey != e.Key || _activeMods != mods)
        {
            // A different anchor resets the cycle, as specified - and so does the
            // same anchor under a different chord, which is a different hotkey
            // asking for its own first size rather than the next one along.
            _activeKey = e.Key;
            _activeMods = mods;
            _cycleIndex = 0;
        }

        if (mods.HasFlag(ChordModifiers.Win)) _winConsumed = true;

        var fire = _rings.TryGetValue(key, out var ring) && ring.Count > 0 ? ring[0] : action;
        return new HookDecision(SwallowWith(mods), fire);
    }

    /// <summary>Step to the next entry in this key's ring, wrapping at the end.</summary>
    private HotkeyAction? Advance((ChordModifiers Mods, KeyStroke Key) key)
    {
        if (_rings.TryGetValue(key, out var ring) && ring.Count > 0)
        {
            _cycleIndex = (_cycleIndex + 1) % ring.Count;
            return ring[_cycleIndex];
        }

        return _bindings.GetValueOrDefault(key);
    }

    private HookAction SwallowWith(ChordModifiers mods)
    {
        var action = HookAction.Swallow;
        if (mods.HasFlag(ChordModifiers.Win) && Suppression == WinKeySuppression.DummyKey)
            action |= HookAction.InjectDummyKey;
        return action;
    }

    private static bool IsWin(ushort vk) => vk is VkLWin or VkRWin;

    private static bool IsModifier(ushort vk) => vk is
        VkLShift or VkRShift or VkLControl or VkRControl or
        VkLMenu or VkRMenu or VkLWin or VkRWin or
        VkShift or VkControl or VkMenu;
}
