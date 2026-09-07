using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Where the pointer is, which window is under the drag, and how big that window
/// currently is - the size being how a move is told from a resize, since both
/// raise the same events.
/// </summary>
public sealed record DragState(nint Hwnd, int X, int Y, int Width, int Height);

/// <summary>
/// Watches window drags, so a window can be dropped into a zone.
/// <para>
/// Uses SetWinEventHook out-of-context, like <see cref="ForegroundWatcher"/>:
/// events arrive on our own thread and nothing is injected into other processes.
/// </para>
/// <para>
/// EVENT_SYSTEM_MOVESIZESTART fires when a window enters the standard modal move
/// loop, which is most of them but deliberately not all: an app that draws its
/// own title bar and drags itself - Chrome, Electron, some Qt apps - never enters
/// that loop and raises no event. Those windows simply do not offer drag-to-snap;
/// their hotkeys work exactly as before. Catching them needs a low-level mouse
/// hook watching for a press over a caption, which is a second global hook and a
/// second latency budget, and is not worth adding until this proves too narrow
/// in practice.
/// </para>
/// <para>
/// Position comes from the cursor rather than the window rect. A window is
/// dragged from wherever it was grabbed, so its top-left says nothing about where
/// the user is pointing, and dropping is about where the pointer is.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class DragWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    private WinEventProc? _moveSize;   // both held so the GC cannot collect them
    private WinEventProc? _location;
    private nint _moveSizeHook;
    private nint _locationHook;
    private uint _threadId;
    private nint _dragging;

    public DragWatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Mullion.DragWatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public event Action<DragState>? DragStarted;

    public event Action<DragState>? DragMoved;

    public event Action<DragState>? DragEnded;

    /// <summary>True while a window is in a move loop.</summary>
    public bool IsDragging => _dragging != 0;

    private void Run()
    {
        _threadId = WindowClass.GetCurrentThreadId();
        _moveSize = OnMoveSize;
        _location = OnLocationChanged;

        _moveSizeHook = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
            0, _moveSize, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        // Location changes are noisy - every window that moves anywhere raises
        // them - so the callback filters to the one being dragged. Hooking only
        // that window's thread would be tighter, but the thread is not known
        // until the drag starts and re-hooking mid-drag races the drag itself.
        _locationHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            0, _location, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        _ready.Set();

        while (WindowClass.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            WindowClass.TranslateMessage(ref msg);
            WindowClass.DispatchMessage(ref msg);
        }

        if (_moveSizeHook != 0) { UnhookWinEvent(_moveSizeHook); _moveSizeHook = 0; }
        if (_locationHook != 0) { UnhookWinEvent(_locationHook); _locationHook = 0; }
    }

    private void OnMoveSize(
        nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == 0) return;

        try
        {
            if (evt == EVENT_SYSTEM_MOVESIZESTART)
            {
                // A resize raises the same event as a move. Only a move can be
                // dropped into a zone, and the two are told apart by whether the
                // window's size changes - which is not known yet, so the drop
                // side checks it at the end instead.
                _dragging = hwnd;
                DragStarted?.Invoke(At(hwnd));
                return;
            }

            if (_dragging == 0) return;

            var ended = At(_dragging);
            _dragging = 0;
            DragEnded?.Invoke(ended);
        }
        catch { /* a watcher must never take the app down */ }
    }

    private void OnLocationChanged(
        nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == 0 || hwnd != _dragging) return;

        try { DragMoved?.Invoke(At(hwnd)); }
        catch { /* as above */ }
    }

    private static DragState At(nint hwnd)
    {
        GetCursorPos(out var cursor);

        var width = 0;
        var height = 0;

        if (Win.GetWindowRect(hwnd, out var rect))
        {
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        return new DragState(hwnd, cursor.X, cursor.Y, width, height);
    }

    public void Dispose()
    {
        if (_threadId != 0) WindowClass.PostThreadMessage(_threadId, WindowClass.WM_QUIT, 0, 0);

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        GC.KeepAlive(_moveSize);
        GC.KeepAlive(_location);
    }
}
