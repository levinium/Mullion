using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Mullion.Core.Hotkeys;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Hotkeys;

public sealed record HookHealth(
    bool Installed,
    long ReinstallCount,
    double LatencyP50Ms,
    double LatencyMaxMs,
    int LowLevelHooksTimeoutMs,
    bool Elevated);

/// <summary>
/// Owns the WH_KEYBOARD_LL hook on a dedicated thread with its own message pump.
/// <para>
/// This is the only way to bind Win+A and friends: RegisterHotKey cannot claim
/// shell-reserved Win combinations, but a low-level hook sits ahead of the shell
/// in the input chain and can swallow them.
/// </para>
/// <para>
/// The callback is deliberately thin - a frozen-dictionary lookup and a lock-free
/// channel write, nothing else. If it overruns LowLevelHooksTimeout (1000ms by
/// default) Windows silently uninstalls the hook with no error, and every hotkey
/// stops working until something notices. Hence: no allocation, no logging, no
/// locks, no LINQ on this path, and a watchdog with four independent triggers.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly HotkeyStateMachine _machine;
    private readonly Channel<HotkeyAction> _queue = Channel.CreateBounded<HotkeyAction>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = true,
        });

    // Held in a field so the GC cannot collect the delegate while Windows holds
    // a native pointer to it - a classic and very hard to diagnose crash.
    private Hooks.HookProc? _callback;

    private readonly INPUT[] _dummyInput = new INPUT[2];

    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private Timer? _watchdog;

    private long _lastCallbackTicks;
    private long _reinstalls;
    private long _maxLatencyTicks;
    private readonly long[] _latencySamples = new long[64];
    private int _latencyIndex;

    public LowLevelKeyboardHook(HotkeyStateMachine machine)
    {
        _machine = machine;

        _dummyInput[0] = new INPUT
        {
            type = Hooks.INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = Hooks.VK_NONE, dwExtraInfo = Hooks.SentinelExtraInfo },
            },
        };

        _dummyInput[1] = _dummyInput[0];
        _dummyInput[1].u.ki.dwFlags = Hooks.KEYEVENTF_KEYUP;
    }

    public ChannelReader<HotkeyAction> Actions => _queue.Reader;

    public bool IsInstalled => _hook != 0;

    public event Action<string>? Diagnostic;

    public void Start()
    {
        if (_thread is not null) return;

        var ready = new ManualResetEventSlim(false);

        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "Mullion.Hook",

            // Reduces the chance that scheduling delay pushes a callback past
            // LowLevelHooksTimeout under load.
            Priority = ThreadPriority.AboveNormal,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Keyboard hook thread did not start.");

        // Four independent recovery triggers, because a hook that dies silently
        // is what makes tools in this category feel unreliable.
        _watchdog = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = WindowClass.GetCurrentThreadId();
        Install();
        ready.Set();

        while (WindowClass.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            // Handle thread messages HERE. PostThreadMessage delivers with
            // hwnd == 0, and DispatchMessage silently discards those because
            // there is no window procedure to route them to - this thread owns
            // no window. Relying on DispatchMessage meant the watchdog posted
            // re-hook requests that were never acted on, so it re-posted every
            // couple of seconds forever and the hook was never actually
            // reinstalled.
            if (msg.hwnd == 0 && msg.message == WatchdogRehook)
            {
                Reinstall();
                continue;
            }

            WindowClass.TranslateMessage(ref msg);
            WindowClass.DispatchMessage(ref msg);
        }

        Uninstall();
    }

    private void Install()
    {
        if (_hook != 0) return;

        _callback = HookCallback;
        _hook = Hooks.SetWindowsHookEx(
            Hooks.WH_KEYBOARD_LL, _callback, WindowClass.GetModuleHandle(null), 0);

        if (_hook == 0)
            Diagnostic?.Invoke($"SetWindowsHookEx failed ({Marshal.GetLastWin32Error()}).");
        else
            Volatile.Write(ref _lastCallbackTicks, Stopwatch.GetTimestamp());
    }

    private void Uninstall()
    {
        if (_hook == 0) return;
        Hooks.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    /// <summary>
    /// Install the replacement BEFORE removing the old one, so there is never an
    /// instant with no hook.
    /// <para>
    /// Unhooking first leaves a gap, and a keypress landing in that gap reaches
    /// the shell untouched - which for a Win chord means the Start menu opens
    /// and the hotkey does nothing. That is exactly what happened while a
    /// misfiring watchdog was re-arming every couple of seconds.
    /// </para>
    /// <para>
    /// Both hooks are briefly in the chain, but that is harmless: the newer one
    /// runs first and a swallowed key never reaches the older.
    /// </para>
    /// </summary>
    private void Reinstall()
    {
        var previous = _hook;
        var previousCallback = _callback;

        _hook = 0;
        Install();

        if (_hook == 0)
        {
            // The replacement failed; keep the old one rather than ending up
            // with none at all.
            _hook = previous;
            _callback = previousCallback;
            return;
        }

        if (previous != 0) Hooks.UnhookWindowsHookEx(previous);

        GC.KeepAlive(previousCallback);
        Interlocked.Increment(ref _reinstalls);
    }

    private unsafe nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode != Hooks.HC_ACTION) return Hooks.CallNextHookEx(0, nCode, wParam, lParam);

        var start = Stopwatch.GetTimestamp();

        try
        {
            // Read directly from the native pointer: Marshal.PtrToStructure
            // allocates, and allocation on this path risks a GC pause pushing
            // the callback past LowLevelHooksTimeout.
            var kb = Unsafe.Read<KBDLLHOOKSTRUCT>((void*)lParam);
            var message = (uint)wParam;

            var ev = new KeyEvent(
                ScanCode: (ushort)kb.scanCode,
                VirtualKey: (ushort)kb.vkCode,
                IsKeyUp: message is Hooks.WM_KEYUP or Hooks.WM_SYSKEYUP,
                IsInjected: (kb.flags & Hooks.LLKHF_INJECTED) != 0,
                IsOurInjection: kb.dwExtraInfo == Hooks.SentinelExtraInfo,
                TimestampMs: kb.time);

            var decision = _machine.Process(in ev);

            if ((decision.Action & HookAction.InjectDummyKey) != 0)
                Hooks.SendInput(2, _dummyInput, Marshal.SizeOf<INPUT>());

            if (decision.Fire is not null) _queue.Writer.TryWrite(decision.Fire);

            if ((decision.Action & HookAction.Swallow) != 0) return 1;
        }
        catch
        {
            // An exception escaping a hook callback is fatal to the process.
            // There is nothing useful to do here and no safe way to log.
        }
        finally
        {
            var elapsed = Stopwatch.GetTimestamp() - start;
            Volatile.Write(ref _lastCallbackTicks, Stopwatch.GetTimestamp());
            RecordLatency(elapsed);
        }

        return Hooks.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void RecordLatency(long ticks)
    {
        var index = Interlocked.Increment(ref _latencyIndex) & (_latencySamples.Length - 1);
        Volatile.Write(ref _latencySamples[index], ticks);

        var max = Volatile.Read(ref _maxLatencyTicks);
        if (ticks > max) Volatile.Write(ref _maxLatencyTicks, ticks);
    }

    private void Tick()
    {
        try
        {
            var now = Stopwatch.GetTimestamp();
            var sinceCallback = (now - Volatile.Read(ref _lastCallbackTicks)) / (double)Stopwatch.Frequency;

            // Trigger 1: a callback that ran long enough to risk the timeout.
            if (TicksToMs(Volatile.Read(ref _maxLatencyTicks)) > 180)
            {
                Volatile.Write(ref _maxLatencyTicks, 0);
                Post(WatchdogRehook);
                Diagnostic?.Invoke("Hook callback exceeded 180ms; reinstalling as a precaution.");
                return;
            }

            // There used to be a second trigger here: "silent for 5s while
            // GetLastInputInfo says the user was active". It was unsound and
            // fired constantly. GetLastInputInfo counts MOUSE input too, but a
            // keyboard hook only ever sees keys - so any stretch of mouse-only
            // work looked exactly like a dead hook. Logging caught it
            // reinstalling twice within eight seconds of a normal startup.
            //
            // There is no cheap way to ask "when was the last KEY pressed"
            // without the hook that is in question, so the heuristic is gone
            // rather than papered over with a longer timeout.

            // Trigger 2: unconditional re-arm. Idempotent, microseconds, and the
            // safety net for every failure mode not anticipated above. 30s
            // bounds worst-case recovery without churning the hook chain.
            if (sinceCallback > 30)
            {
                Post(WatchdogRehook);
                Diagnostic?.Invoke($"Hook idle for {sinceCallback:0}s; re-arming as a precaution.");
            }
        }
        catch
        {
            // The watchdog must never be the thing that takes the app down.
        }
    }

    private const uint WatchdogRehook = WindowClass.WM_APP + 3;

    private void Post(uint message)
    {
        if (_threadId != 0) WindowClass.PostThreadMessage(_threadId, message, 0, 0);
    }

    private static bool UserActiveWithin(double seconds)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!Hooks.GetLastInputInfo(ref info)) return true;

        var idleMs = Environment.TickCount - (long)info.dwTime;
        return idleMs < seconds * 1000;
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>Physical modifier state, used only to recover from missed key-ups.</summary>
    public static ChordModifiers ReadPhysicalModifiers()
    {
        var mods = ChordModifiers.None;
        if ((Hooks.GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= ChordModifiers.Shift;
        if ((Hooks.GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= ChordModifiers.Control;
        if ((Hooks.GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= ChordModifiers.Alt;
        if ((Hooks.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Hooks.GetAsyncKeyState(0x5C) & 0x8000) != 0)
            mods |= ChordModifiers.Win;
        return mods;
    }

    public HookHealth GetHealth()
    {
        var samples = _latencySamples.Where(t => t > 0).Order().ToArray();
        var p50 = samples.Length > 0 ? TicksToMs(samples[samples.Length / 2]) : 0;

        return new HookHealth(
            IsInstalled,
            Interlocked.Read(ref _reinstalls),
            p50,
            TicksToMs(Volatile.Read(ref _maxLatencyTicks)),
            ReadLowLevelHooksTimeout(),
            Windows.Elevation.IsCurrentProcessElevated);
    }

    /// <summary>
    /// The budget a callback has before Windows drops the hook. Reading it makes
    /// the constraint visible on the diagnostics page rather than folklore.
    /// </summary>
    private static int ReadLowLevelHooksTimeout()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return key?.GetValue("LowLevelHooksTimeout") is int v ? v : 1000;
        }
        catch
        {
            return 1000;
        }
    }

    public void Dispose()
    {
        _watchdog?.Dispose();
        _watchdog = null;

        if (_threadId != 0) WindowClass.PostThreadMessage(_threadId, WindowClass.WM_QUIT, 0, 0);

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;

        _queue.Writer.TryComplete();
        GC.KeepAlive(_callback);
    }
}
