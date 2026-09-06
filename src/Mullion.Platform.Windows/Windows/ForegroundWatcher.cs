using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Windows;

/// <summary>Why hotkeys are not currently able to act, or null when they are.</summary>
public sealed record ReachState(bool Reachable, string? Reason, string? WindowTitle);

/// <summary>
/// Watches which window has focus, so the app can say when hotkeys cannot work.
/// <para>
/// This exists because of a failure that is otherwise completely silent. While
/// a window running as administrator has focus, a medium-integrity keyboard
/// hook receives NO key events at all - not the keypress, not an error, nothing.
/// Mullion cannot report a failed hotkey because it never learns a key was
/// pressed. The only way to explain it is to notice the focus change instead.
/// </para>
/// <para>
/// Uses SetWinEventHook out-of-context, which delivers on our own thread and
/// does not inject into other processes.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ForegroundWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private delegate void WinEventProc(
        nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWinEventHook(
        uint min, uint max, nint hmodWinEventProc, WinEventProc callback,
        uint idProcess, uint idThread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    private WinEventProc? _callback;   // held so the GC cannot collect it
    private nint _hook;
    private uint _threadId;
    private ReachState _state = new(true, null, null);

    public ForegroundWatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Mullion.ForegroundWatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Raised when reachability changes, not on every focus change.</summary>
    public event Action<ReachState>? ReachChanged;

    public ReachState Current => _state;

    private void Run()
    {
        _threadId = WindowClass.GetCurrentThreadId();
        _callback = OnForegroundChanged;

        _hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            0, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        // Establish the starting state rather than waiting for the first switch.
        Evaluate(Win.GetForegroundWindow());
        _ready.Set();

        while (WindowClass.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            WindowClass.TranslateMessage(ref msg);
            WindowClass.DispatchMessage(ref msg);
        }

        if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; }
    }

    private void OnForegroundChanged(
        nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // idObject OBJID_WINDOW == 0; anything else is a child element.
        if (idObject != 0 || hwnd == 0) return;

        try { Evaluate(hwnd); }
        catch { /* a watcher must never take the app down */ }
    }

    private void Evaluate(nint hwnd)
    {
        if (hwnd == 0) return;

        var reachable = !Elevation.IsOutOfReach(hwnd);

        var next = reachable
            ? new ReachState(true, null, null)
            : new ReachState(false,
                "runs as administrator, so Windows hides its keystrokes from Mullion",
                TitleOf(hwnd));

        // Only report transitions: the foreground changes constantly, and a
        // message on every switch would be noise rather than information.
        if (next.Reachable == _state.Reachable && next.WindowTitle == _state.WindowTitle) return;

        _state = next;
        ReachChanged?.Invoke(next);
    }

    private static string TitleOf(nint hwnd)
    {
        var buffer = new char[256];
        var length = Win.GetWindowText(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "an elevated window";
    }

    public void Dispose()
    {
        if (_threadId != 0) WindowClass.PostThreadMessage(_threadId, WindowClass.WM_QUIT, 0, 0);

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        GC.KeepAlive(_callback);
    }
}
