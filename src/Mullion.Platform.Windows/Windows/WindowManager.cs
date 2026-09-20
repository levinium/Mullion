using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Core.Abstractions;
using Mullion.Core.Geometry;
using Mullion.Core.Layout;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Moves the foreground window into a target rectangle.
/// <para>
/// Runs on the executor thread, never the hook callback: it makes blocking Win32
/// calls and waits for windows to settle, which would blow the low-level hook
/// timeout and get the hook silently uninstalled.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowManager(int undoDepth = 20) : IWindowManager
{
    private readonly Stack<WindowSnapshot> _undo = new();
    private readonly int _undoDepth = Math.Max(1, undoDepth);

    /// <summary>
    /// Watches what every move does to a window, so it can be put back to the
    /// size its owner last chose for it.
    /// <para>
    /// Here rather than beside the drag handling because moves arrive from two
    /// places - hotkeys and drops - and a memory that only saw one of them
    /// could not answer for a window filled by the other. That was the bug:
    /// filling a zone with Win+A left nothing to toggle back to.
    /// </para>
    /// </summary>
    private readonly WindowSizeMemory _sizes = new();

    /// <summary>Shell and desktop classes that must never be moved.</summary>
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "MullionOverlay",
    };

    public int UndoDepth => _undo.Count;

    public MoveResult MoveForegroundTo(PxRect target)
    {
        var hwnd = Win.GetForegroundWindow();
        return hwnd == 0
            ? new MoveResult(MoveOutcome.SkippedNoForegroundWindow, default, 0)
            : MoveWindowTo(hwnd, target);
    }

    public PxRect? ChosenSizeOf(nint hwnd)
    {
        var root = Win.GetAncestor(hwnd, Win.GA_ROOT);

        return _sizes.ChosenSizeOf(root != 0 ? root : hwnd);
    }

    public PxRect? BoundsOf(nint hwnd)
    {
        var root = Win.GetAncestor(hwnd, Win.GA_ROOT);
        if (root != 0) hwnd = root;

        if (!Win.IsWindow(hwnd)) return null;

        // The same measure a move is verified against, so "is it already
        // filling this zone" is asked in the units the answer was written in.
        var bounds = GetFrameBounds(hwnd);

        return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
    }

    /// <summary>
    /// Move a specific window. Separated from <see cref="MoveForegroundTo"/> so
    /// the pipeline can be exercised against a known window in tests without
    /// depending on which window happens to have focus.
    /// </summary>
    public MoveResult MoveWindowTo(nint hwnd, PxRect target)
    {
        var root = Win.GetAncestor(hwnd, Win.GA_ROOT);
        if (root != 0) hwnd = root;

        var gate = Eligible(hwnd);
        if (gate is not null) return new MoveResult(gate.Value, default, 0);

        // Check integrity BEFORE attempting. SetWindowPos would fail with
        // ERROR_ACCESS_DENIED anyway, but checking first lets us name the
        // offending window in the message instead of reporting a bare error
        // code, and avoids leaving a half-applied restore behind.
        if (Elevation.IsOutOfReach(hwnd))
        {
            return new MoveResult(
                MoveOutcome.FailedAccessDenied, default, 0,
                $"\"{TitleOf(hwnd)}\" runs elevated and Mullion does not, so Windows blocks the move. " +
                "Restart Mullion as administrator to manage elevated windows.");
        }

        var resizable = IsResizable(hwnd);

        PushUndo(hwnd);

        // A maximized window silently ignores SetWindowPos, and reading its rect
        // during the restore animation returns garbage - so restore, then settle.
        if (Win.IsZoomed(hwnd) || Win.IsIconic(hwnd))
        {
            Win.ShowWindow(hwnd, Win.SW_RESTORE);
            WaitForSettle(hwnd, 150);
        }

        // Note the size the window has right now, before anything of ours
        // changes it. If it is not the size we last gave it, its owner has
        // resized it since, and this is the size to bring it back to.
        _sizes.Observe(hwnd, GetFrameBounds(hwnd));

        // A window that cannot be resized is centered at its current size rather
        // than skipped: a dialog that ignores the hotkey reads as a broken app,
        // whereas centering reads as intentional.
        if (!resizable)
        {
            var current = GetFrameBounds(hwnd);
            var centered = new PxRect(
                target.Left + (target.Width - current.Width) / 2,
                target.Top + (target.Height - current.Height) / 2,
                current.Width,
                current.Height);

            var placed = Apply(hwnd, centered, out _);
            if (!placed) return new MoveResult(MoveOutcome.FailedUnknown, default, 1);

            return Placed(
                hwnd, MoveOutcome.Centered, 1,
                "Window is not resizable; centered at its current size.");
        }

        var attempts = 0;
        var lastError = 0;
        var previousOversize = false;

        for (var i = 0; i < 3; i++)
        {
            attempts++;

            if (!Apply(hwnd, target, out lastError))
            {
                return lastError switch
                {
                    Win.ERROR_ACCESS_DENIED => new MoveResult(
                        MoveOutcome.FailedAccessDenied, default, attempts,
                        "The focused window belongs to an elevated process. Restart Mullion as administrator to move it."),
                    Win.ERROR_INVALID_WINDOW_HANDLE => new MoveResult(
                        MoveOutcome.SkippedInvalid, default, attempts),
                    _ => new MoveResult(MoveOutcome.FailedUnknown, default, attempts, $"SetWindowPos failed ({lastError})."),
                };
            }

            WaitForSettle(hwnd, 80);
            var achieved = GetFrameBounds(hwnd);

            var error = Math.Max(
                Math.Max(Math.Abs(achieved.Left - target.Left), Math.Abs(achieved.Top - target.Top)),
                Math.Max(Math.Abs(achieved.Width - target.Width), Math.Abs(achieved.Height - target.Height)));

            if (error <= 2) return Placed(hwnd, MoveOutcome.Moved, attempts);

            // A window with a hard minimum size never converges. Detect it after
            // two consecutive oversized results and center what we got instead
            // of looping.
            var oversize = achieved.Width > target.Width + 2 || achieved.Height > target.Height + 2;
            if (oversize && previousOversize)
            {
                var centered = new PxRect(
                    target.Left + (target.Width - achieved.Width) / 2,
                    target.Top + (target.Height - achieved.Height) / 2,
                    achieved.Width,
                    achieved.Height);

                Apply(hwnd, centered, out _);
                return Placed(
                    hwnd, MoveOutcome.MovedApproximate, attempts,
                    $"Window enforces a minimum size of {achieved.Width}x{achieved.Height}; centered in the zone.");
            }

            previousOversize = oversize;
        }

        return Placed(
            hwnd, MoveOutcome.MovedApproximate, attempts,
            "Window did not settle on the requested bounds.");
    }

    /// <summary>
    /// A move that landed, recorded as ours before it is reported.
    /// <para>
    /// Every successful exit goes through here so none can forget: a single
    /// path that skipped it would leave that window's remembered size stale
    /// forever, and the toggle would put it somewhere it had not been in
    /// hours.
    /// </para>
    /// </summary>
    private MoveResult Placed(nint hwnd, MoveOutcome outcome, int attempts, string? note = null)
    {
        var achieved = GetFrameBounds(hwnd);

        _sizes.Applied(hwnd, achieved);

        return new MoveResult(outcome, achieved, attempts, note);
    }

    /// <summary>
    /// Minimize whatever has focus.
    /// <para>
    /// Through the same eligibility gate as a move, so the hotkey cannot minimize
    /// the desktop or a shell window - pressed with nothing but the wallpaper in
    /// front of you, the honest answer is to do nothing.
    /// </para>
    /// <para>
    /// Recorded on the undo stack like a move. It was not, on the reasoning that
    /// Windows already remembers where a minimized window came from - which is
    /// true, and beside the point: that is what restoring it from the taskbar
    /// uses. The undo key means "take back what Mullion just did", and minimizing
    /// a window is one of the things Mullion does. Costing one more press to
    /// reach the move before it is how an undo stack is supposed to behave.
    /// </para>
    /// </summary>
    public bool MinimizeForeground()
    {
        var hwnd = Win.GetForegroundWindow();
        if (hwnd == 0) return false;

        var root = Win.GetAncestor(hwnd, Win.GA_ROOT);
        if (root != 0) hwnd = root;

        if (Eligible(hwnd) is not null) return false;

        // Before minimizing, so the snapshot holds the placement it had while
        // still on screen. Taken afterwards it would record the minimized state
        // and undo would restore it to being minimized.
        var recorded = PushUndo(hwnd);

        if (Win.ShowWindow(hwnd, Win.SW_MINIMIZE)) return true;

        // Nothing happened, so there is nothing to take back. Leaving the entry
        // would spend the next undo putting a window back where it already is
        // and lose the move underneath it - but withdraw only an entry this call
        // actually made, or the pop lands on somebody else's.
        if (recorded) _undo.TryPop(out _);
        return false;
    }

    public bool UndoLastMove()
    {
        while (_undo.Count > 0)
        {
            var snap = _undo.Pop();

            // HWNDs are recycled, so confirm this is still the same window
            // before restoring - otherwise undo can resize an unrelated app.
            if (!Win.IsWindow(snap.Handle)) continue;

            Win.GetWindowThreadProcessId(snap.Handle, out var pid);
            if (pid != snap.ProcessId) continue;
            if (ClassNameOf(snap.Handle) != snap.ClassName) continue;

            // Asked BEFORE restoring, because restoring is what stops it being
            // true. A window coming back from the taskbar should be the one you
            // are looking at; a window merely being moved back should not steal
            // focus from whatever you have since switched to.
            var wasMinimized = Win.IsIconic(snap.Handle);

            var placement = FromBlob(snap.PlacementBlob);
            Win.SetWindowPlacement(snap.Handle, ref placement);
            WaitForSettle(snap.Handle, 120);

            if (snap.WasMaximized)
            {
                Win.ShowWindow(snap.Handle, Win.SW_SHOWMAXIMIZED);
            }
            else
            {
                // SetWindowPlacement works in workspace coordinates, which differ
                // from screen coordinates when a taskbar is present, so reapply
                // the recorded screen rect for an exact restore.
                Apply(snap.Handle, snap.ScreenRect, out _);
            }

            if (wasMinimized) Activate(snap.Handle);

            return true;
        }

        return false;
    }

    // ---- internals ---------------------------------------------------------

    private static MoveOutcome? Eligible(nint hwnd)
    {
        if (!Win.IsWindow(hwnd) || !Win.IsWindowVisible(hwnd)) return MoveOutcome.SkippedInvalid;
        if (hwnd == Win.GetShellWindow() || hwnd == Win.GetDesktopWindow()) return MoveOutcome.SkippedShellWindow;
        if (ExcludedClasses.Contains(ClassNameOf(hwnd))) return MoveOutcome.SkippedShellWindow;

        var style = (long)Win.GetWindowLongPtr(hwnd, Win.GWL_STYLE);
        if ((style & Win.WS_CHILD) != 0) return MoveOutcome.SkippedInvalid;

        // UWP keeps hidden cloaked windows around; a naive implementation moves
        // those ghosts instead of the window the user is actually looking at.
        if (Win.DwmGetWindowAttributeInt(hwnd, Win.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return MoveOutcome.SkippedCloaked;

        return null;
    }

    private static bool IsResizable(nint hwnd)
    {
        var style = (long)Win.GetWindowLongPtr(hwnd, Win.GWL_STYLE);
        return (style & Win.WS_THICKFRAME) != 0 || (style & Win.WS_MAXIMIZEBOX) != 0;
    }

    /// <summary>
    /// Position the window so its VISIBLE frame lands on <paramref name="target"/>,
    /// compensating for the parts of the frame the user cannot see: the invisible
    /// resize border that GetWindowRect includes, and the translucent border DWM
    /// paints inside the extended frame bounds. Without the first every window
    /// sits ~7-11px inset; without the second, one pixel of it.
    /// </summary>
    private static bool Apply(nint hwnd, PxRect target, out int lastError)
    {
        var pad = FramePadding(hwnd);

        var x = target.Left - pad.Left;
        var y = target.Top - pad.Top;
        var w = target.Width + pad.Left + pad.Right;
        var h = target.Height + pad.Top + pad.Bottom;

        lastError = 0;
        if (Win.SetWindowPos(hwnd, 0, x, y, w, h,
                Win.SWP_NOZORDER | Win.SWP_NOACTIVATE | Win.SWP_NOOWNERZORDER))
            return true;

        lastError = Marshal.GetLastWin32Error();
        return false;
    }

    private readonly record struct Padding(int Left, int Top, int Right, int Bottom);

    private static Padding FramePadding(nint hwnd)
    {
        if (!Win.GetWindowRect(hwnd, out var wr)) return default;
        if (Win.DwmGetWindowAttributeRect(hwnd, Win.DWMWA_EXTENDED_FRAME_BOUNDS, out var ef, Marshal.SizeOf<RECT>()) != 0)
            return default;

        var border = BorderThickness(hwnd);

        var pad = new Padding(
            ef.Left + border - wr.Left,
            ef.Top + border - wr.Top,
            wr.Right - (ef.Right - border),
            wr.Bottom - (ef.Bottom - border));

        // Reject nonsense from windows caught mid-animation, over RDP, or with
        // exotic frames rather than trusting it and flinging the window away.
        const int MaxPad = 32;
        if (pad.Left < 0 || pad.Top < 0 || pad.Right < 0 || pad.Bottom < 0 ||
            pad.Left > MaxPad || pad.Top > MaxPad || pad.Right > MaxPad || pad.Bottom > MaxPad)
            return default;

        return pad;
    }

    /// <summary>
    /// Where the window's opaque edges are - the rectangle a person would trace
    /// around it. Every read-back, undo snapshot and zone-fit test goes through
    /// here, so it is measured the same way the targets handed to
    /// <see cref="Apply"/> are; a mismatch of even a pixel between the two would
    /// have each move nudge the window one further.
    /// </summary>
    private static PxRect GetFrameBounds(nint hwnd)
    {
        if (Win.DwmGetWindowAttributeRect(hwnd, Win.DWMWA_EXTENDED_FRAME_BOUNDS, out var ef, Marshal.SizeOf<RECT>()) == 0)
        {
            var border = BorderThickness(hwnd);
            return PxRect.FromLtrb(
                ef.Left + border, ef.Top + border, ef.Right - border, ef.Bottom - border);
        }

        return Win.GetWindowRect(hwnd, out var wr)
            ? PxRect.FromLtrb(wr.Left, wr.Top, wr.Right, wr.Bottom)
            : default;
    }

    /// <summary>
    /// The border DWM paints around a window, in physical pixels.
    /// <para>
    /// It is drawn INSIDE the extended frame bounds and it is translucent, so a
    /// window sized exactly to its zone still shows a pixel of whatever is
    /// behind it along every edge - and two windows in adjacent zones show two,
    /// one from each side of the seam. Counting the border as frame rather than
    /// as content is what closes those gaps.
    /// </para>
    /// <para>
    /// Asked per window rather than hardcoded to 1: this is a physical
    /// measurement, so it grows with the scaling of the display the window is
    /// on, and DWM is the only thing that knows whether it drew a border at all.
    /// </para>
    /// </summary>
    private static int BorderThickness(nint hwnd)
    {
        // Unsupported before Windows 11, where the call fails and leaves the
        // value untouched. Zero is the honest answer there: no correction, and
        // behavior identical to never having asked.
        if (Win.DwmGetWindowAttributeInt(
                hwnd, Win.DWMWA_VISIBLE_FRAME_BORDER_THICKNESS, out var thickness, sizeof(int)) != 0)
            return 0;

        // Windows answers 0xFFFFFFFF - read back as -1 - for a window it draws
        // no border around, and the value scales with DPI, so bound it rather
        // than trust it. Anything outside the range means "do not correct".
        const int MaxBorder = 8;
        return thickness is >= 0 and <= MaxBorder ? thickness : 0;
    }

    /// <summary>Poll until two consecutive reads agree, or the budget runs out. Never a fixed sleep.</summary>
    private static void WaitForSettle(nint hwnd, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        var previous = GetFrameBounds(hwnd);

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(8);
            var current = GetFrameBounds(hwnd);
            if (current == previous) return;
            previous = current;
        }
    }

    /// <summary>
    /// Bring a window to the front and give it the keyboard.
    /// <para>
    /// Restoring a minimized window puts it back on screen without focus, which
    /// leaves it sitting behind whatever was in front - so undoing a minimize
    /// appeared to do nothing until you went looking for the window.
    /// </para>
    /// <para>
    /// SetForegroundWindow alone is not enough. Windows only lets the process
    /// that owns the foreground window hand focus away; from a background app the
    /// call quietly flashes the taskbar button instead. Attaching our input queue
    /// to the foreground thread's makes us part of it for the length of the call,
    /// which is the long-standing way through. Detached again immediately: two
    /// threads sharing an input queue also share focus and key state, and leaving
    /// that in place would be a far stranger bug than the one being fixed.
    /// </para>
    /// </summary>
    private static void Activate(nint hwnd)
    {
        // SW_RESTORE rather than SW_SHOW: it un-minimizes and activates, and is
        // harmless on a window that is already up.
        Win.ShowWindow(hwnd, Win.SW_RESTORE);

        if (Win.SetForegroundWindow(hwnd)) return;

        var foreground = Win.GetForegroundWindow();
        if (foreground == 0) return;

        var theirs = Win.GetWindowThreadProcessId(foreground, out _);
        var ours = WindowClass.GetCurrentThreadId();

        if (theirs == 0 || theirs == ours) return;
        if (!Win.AttachThreadInput(ours, theirs, true)) return;

        try
        {
            Win.SetForegroundWindow(hwnd);
        }
        finally
        {
            Win.AttachThreadInput(ours, theirs, false);
        }
    }

    /// <summary>
    /// Record where a window is, so the move about to happen can be taken back.
    /// <para>
    /// Reports whether anything was actually recorded. A window whose placement
    /// cannot be read leaves the stack untouched, and a caller that later wants
    /// to withdraw its own entry must not pop somebody else's.
    /// </para>
    /// </summary>
    private bool PushUndo(nint hwnd)
    {
        var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!Win.GetWindowPlacement(hwnd, ref placement)) return false;

        Win.GetWindowThreadProcessId(hwnd, out var pid);

        var snapshot = new WindowSnapshot(
            hwnd,
            pid,
            ClassNameOf(hwnd),
            TitleOf(hwnd),
            GetFrameBounds(hwnd),
            Win.IsZoomed(hwnd),
            ToBlob(placement),
            DateTimeOffset.UtcNow);

        _undo.Push(snapshot);

        while (_undo.Count > _undoDepth)
        {
            var kept = _undo.Take(_undoDepth).Reverse().ToArray();
            _undo.Clear();
            foreach (var s in kept) _undo.Push(s);
        }

        return true;
    }

    private static byte[] ToBlob(WINDOWPLACEMENT p)
    {
        var size = Marshal.SizeOf<WINDOWPLACEMENT>();
        var bytes = new byte[size];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Marshal.StructureToPtr(p, handle.AddrOfPinnedObject(), false); }
        finally { handle.Free(); }
        return bytes;
    }

    private static WINDOWPLACEMENT FromBlob(byte[] bytes)
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { return Marshal.PtrToStructure<WINDOWPLACEMENT>(handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    private static string ClassNameOf(nint hwnd)
    {
        var buffer = new char[256];
        var length = Win.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string TitleOf(nint hwnd)
    {
        var buffer = new char[512];
        var length = Win.GetWindowText(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }
}

