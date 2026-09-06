using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Detects whether something is running fullscreen, so hotkeys can stand down.
/// <para>
/// Firing a window move into a fullscreen game is worse than doing nothing: it
/// can drop the game out of exclusive mode, or resize a window the user cannot
/// see to a rectangle they did not ask for. Two independent signals are used
/// because neither catches everything - the shell notification state misses
/// borderless-fullscreen apps, and the geometry check misses true exclusive
/// mode where the window rect is not always what you would expect.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class FullscreenDetector
{
    // QUNS_* from SHQueryUserNotificationState.
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    public static bool IsFullscreenActive(out string? reason)
    {
        reason = null;

        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                switch (state)
                {
                    case QUNS_RUNNING_D3D_FULL_SCREEN:
                        reason = "a fullscreen Direct3D application is running";
                        return true;
                    case QUNS_PRESENTATION_MODE:
                        reason = "presentation mode is active";
                        return true;
                    case QUNS_BUSY:
                        // "Busy" also covers a fullscreen app that has not
                        // declared itself as D3D, so treat it as fullscreen.
                        reason = "an application is running fullscreen";
                        return true;
                }
            }
        }
        catch (DllNotFoundException) { /* fall through to the geometry check */ }
        catch (EntryPointNotFoundException) { /* fall through */ }

        return IsForegroundWindowCoveringItsMonitor(out reason);
    }

    /// <summary>
    /// A borderless-fullscreen window exactly covers its monitor and has no
    /// resizable frame. The shell often reports such a window as normal.
    /// </summary>
    private static bool IsForegroundWindowCoveringItsMonitor(out string? reason)
    {
        reason = null;

        var hwnd = Win.GetForegroundWindow();
        if (hwnd == 0) return false;

        // The desktop and shell always cover the screen; they are not games.
        if (hwnd == Win.GetShellWindow() || hwnd == Win.GetDesktopWindow()) return false;

        if (!Win.GetWindowRect(hwnd, out var wr)) return false;

        var monitor = Win.MonitorFromWindow(hwnd, Win.MONITOR_DEFAULTTONEAREST);
        if (monitor == 0) return false;

        var info = new MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() };
        if (!User32.GetMonitorInfo(monitor, ref info)) return false;

        var covers =
            wr.Left <= info.rcMonitor.Left &&
            wr.Top <= info.rcMonitor.Top &&
            wr.Right >= info.rcMonitor.Right &&
            wr.Bottom >= info.rcMonitor.Bottom;

        if (!covers) return false;

        // A maximized ordinary window also covers its work area, but keeps a
        // resizable frame - and should still be movable.
        var style = (long)Win.GetWindowLongPtr(hwnd, Win.GWL_STYLE);
        if ((style & Win.WS_THICKFRAME) != 0 && wr.Top >= info.rcWork.Top) return false;

        reason = "the focused window covers its whole monitor";
        return true;
    }
}
