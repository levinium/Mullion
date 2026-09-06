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
    long TimestampMs);

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

    private Dictionary<(ChordModifiers Mods, ushort Scan), HotkeyAction> _bindings = [];
    private Dictionary<(ChordModifiers Mods, ushort Scan), IReadOnlyList<HotkeyAction>> _rings = [];

    private ushort _activeScan;
    private int _cycleIndex;
    private bool _winConsumed;
    private bool _capturing;

    public bool Enabled { get; set; } = true;

    public WinKeySuppression Suppression { get; set; } = suppression;

    public bool IgnoreInjected { get; set; } = ignoreInjected;

    public event Action<ChordModifiers, ushort>? ChordCaptured;

    public void SetBindings(
        IReadOnlyDictionary<(ChordModifiers, ushort), HotkeyAction> bindings,
        IReadOnlyDictionary<(ChordModifiers, ushort), IReadOnlyList<HotkeyAction>>? rings = null)
    {
        _bindings = bindings.ToDictionary(kv => kv.Key, kv => kv.Value);
        _rings = rings?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];
        Reset();
    }

    public void BeginCapture() { _capturing = true; }

    public void EndCapture() { _capturing = false; }

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

        _activeScan = 0;
        _cycleIndex = 0;
        _winConsumed = false;
    }

    public void Reset()
    {
        _modifiersDown.Clear();
        _activeScan = 0;
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

    public HookDecision Process(in KeyEvent e)
    {
        // 1. Our own injected events must never be reprocessed, or the dummy key
        //    feeds back into the machine. Checked before the enabled flag so the
        //    guard holds even while paused.
        if (e.IsOurInjection) return HookDecision.Pass;
        if (e.IsInjected && IgnoreInjected) return HookDecision.Pass;

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
                    _activeScan = 0;
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
                if (e.VirtualKey == VkEscape) { _capturing = false; return new HookDecision(HookAction.Swallow); }
                ChordCaptured?.Invoke(CurrentModifiers, e.ScanCode);
            }

            return new HookDecision(HookAction.Swallow);
        }

        var mods = CurrentModifiers;
        var key = (mods, e.ScanCode);

        // 5. Key-up of the key we consumed: swallow so no orphan reaches the app.
        if (e.IsKeyUp)
        {
            if (e.ScanCode == _activeScan) return new HookDecision(HookAction.Swallow);
            return HookDecision.Pass;
        }

        // 6. Auto-repeat. Repeats must be swallowed as well as the original, or
        //    holding the key streams characters into the focused app.
        if (e.ScanCode == _activeScan && _bindings.ContainsKey(key))
        {
            var repeated = Advance(key);
            return new HookDecision(SwallowWith(mods), repeated);
        }

        // 7. Match. Strict modifier equality, so Win+Shift+A never fires Win+A.
        if (!_bindings.TryGetValue(key, out var action)) return HookDecision.Pass;

        if (_activeScan != e.ScanCode)
        {
            // A different anchor resets the cycle, as specified.
            _activeScan = e.ScanCode;
            _cycleIndex = 0;
        }

        if (mods.HasFlag(ChordModifiers.Win)) _winConsumed = true;

        var fire = _rings.TryGetValue(key, out var ring) && ring.Count > 0 ? ring[0] : action;
        return new HookDecision(SwallowWith(mods), fire);
    }

    /// <summary>Step to the next entry in this key's ring, wrapping at the end.</summary>
    private HotkeyAction? Advance((ChordModifiers Mods, ushort Scan) key)
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
