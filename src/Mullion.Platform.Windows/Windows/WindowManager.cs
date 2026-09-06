using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Core.Abstractions;
using Mullion.Core.Geometry;
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

        // A window that cannot be resized is centred at its current size rather
        // than skipped: a dialog that ignores the hotkey reads as a broken app,
        // whereas centring reads as intentional.
        if (!resizable)
        {
            var current = GetFrameBounds(hwnd);
            var centred = new PxRect(
                target.Left + (target.Width - current.Width) / 2,
                target.Top + (target.Height - current.Height) / 2,
                current.Width,
                current.Height);

            var placed = Apply(hwnd, centred, out _);
            return new MoveResult(
                placed ? MoveOutcome.Centred : MoveOutcome.FailedUnknown,
                GetFrameBounds(hwnd), 1,
                "Window is not resizable; centred at its current size.");
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

            if (error <= 2) return new MoveResult(MoveOutcome.Moved, achieved, attempts);

            // A window with a hard minimum size never converges. Detect it after
            // two consecutive oversized results and centre what we got instead
            // of looping.
            var oversize = achieved.Width > target.Width + 2 || achieved.Height > target.Height + 2;
            if (oversize && previousOversize)
            {
                var centred = new PxRect(
                    target.Left + (target.Width - achieved.Width) / 2,
                    target.Top + (target.Height - achieved.Height) / 2,
                    achieved.Width,
                    achieved.Height);

                Apply(hwnd, centred, out _);
                return new MoveResult(
                    MoveOutcome.MovedApproximate, GetFrameBounds(hwnd), attempts,
                    $"Window enforces a minimum size of {achieved.Width}x{achieved.Height}; centred in the zone.");
            }

            previousOversize = oversize;
        }

        return new MoveResult(
            MoveOutcome.MovedApproximate, GetFrameBounds(hwnd), attempts,
            "Window did not settle on the requested bounds.");
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
    /// compensating for the invisible resize border that GetWindowRect includes
    /// but the user cannot see. Without this every window sits ~7-11px inset.
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

        var pad = new Padding(
            ef.Left - wr.Left,
            ef.Top - wr.Top,
            wr.Right - ef.Right,
            wr.Bottom - ef.Bottom);

        // Reject nonsense from windows caught mid-animation, over RDP, or with
        // exotic frames rather than trusting it and flinging the window away.
        const int MaxPad = 32;
        if (pad.Left < 0 || pad.Top < 0 || pad.Right < 0 || pad.Bottom < 0 ||
            pad.Left > MaxPad || pad.Top > MaxPad || pad.Right > MaxPad || pad.Bottom > MaxPad)
            return default;

        return pad;
    }

    private static PxRect GetFrameBounds(nint hwnd)
    {
        if (Win.DwmGetWindowAttributeRect(hwnd, Win.DWMWA_EXTENDED_FRAME_BOUNDS, out var ef, Marshal.SizeOf<RECT>()) == 0)
            return PxRect.FromLtrb(ef.Left, ef.Top, ef.Right, ef.Bottom);

        return Win.GetWindowRect(hwnd, out var wr)
            ? PxRect.FromLtrb(wr.Left, wr.Top, wr.Right, wr.Bottom)
            : default;
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

    private void PushUndo(nint hwnd)
    {
        var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!Win.GetWindowPlacement(hwnd, ref placement)) return;

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

