using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Integrity level of a process, which is what actually governs whether we can
/// touch its windows or see its keystrokes.
/// </summary>
public enum IntegrityLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    System = 4,
}

/// <summary>
/// Windows blocks a lower-integrity process from interfering with a
/// higher-integrity one (UIPI). This affects Mullion in TWO distinct ways, and
/// the second is the one that surprises people:
/// <list type="number">
/// <item>
/// SetWindowPos on a window owned by an elevated process fails with
/// ERROR_ACCESS_DENIED. Visible, recoverable, reported.
/// </item>
/// <item>
/// A WH_KEYBOARD_LL hook in a medium-integrity process receives NO key events
/// at all while an elevated window has focus. The hotkey simply does nothing,
/// with no error to report - so this has to be detected proactively rather than
/// waiting for a failure that never arrives.
/// </item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
public static class Elevation
{
    private static bool? _selfElevated;
    private static IntegrityLevel? _selfIntegrity;

    /// <summary>Whether this process is running with an elevated token.</summary>
    public static bool IsCurrentProcessElevated =>
        _selfElevated ??= QueryElevation(Security.GetCurrentProcess());

    public static IntegrityLevel CurrentIntegrity =>
        _selfIntegrity ??= QueryIntegrity(Security.GetCurrentProcess());

    /// <summary>
    /// The integrity level of the process owning <paramref name="hwnd"/>.
    /// Returns <see cref="IntegrityLevel.High"/> when the process cannot be
    /// opened at all, since access denied is itself the signal.
    /// </summary>
    public static IntegrityLevel IntegrityOfWindow(nint hwnd)
    {
        Win.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return IntegrityLevel.Unknown;

        var process = Security.OpenProcess(Security.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return Marshal.GetLastWin32Error() == Win.ERROR_ACCESS_DENIED
                ? IntegrityLevel.High
                : IntegrityLevel.Unknown;
        }

        try { return QueryIntegrity(process); }
        finally { Security.CloseHandle(process); }
    }

    /// <summary>
    /// True when the window sits above us and we therefore cannot move it or
    /// observe keystrokes destined for it.
    /// </summary>
    public static bool IsOutOfReach(nint hwnd)
    {
        var theirs = IntegrityOfWindow(hwnd);
        if (theirs == IntegrityLevel.Unknown) return false;
        return theirs > CurrentIntegrity;
    }

    /// <summary>
    /// Relaunch elevated via the UAC consent prompt. Returns false when the user
    /// declines - which is a normal outcome, not an error.
    /// </summary>
    public static bool TryRestartElevated(string? arguments = null)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas",
            });

            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ERROR_CANCELLED - the user dismissed the UAC prompt.
            return false;
        }
    }

    private static bool QueryElevation(nint process)
    {
        if (!Security.OpenProcessToken(process, Security.TOKEN_QUERY, out var token)) return false;

        try
        {
            var size = (uint)Marshal.SizeOf<TOKEN_ELEVATION>();
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!Security.GetTokenInformation(token, Security.TokenElevation, buffer, size, out _))
                    return false;

                return Marshal.PtrToStructure<TOKEN_ELEVATION>(buffer).TokenIsElevated != 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { Security.CloseHandle(token); }
    }

    private static IntegrityLevel QueryIntegrity(nint process)
    {
        if (!Security.OpenProcessToken(process, Security.TOKEN_QUERY, out var token))
            return IntegrityLevel.Unknown;

        try
        {
            Security.GetTokenInformation(token, Security.TokenIntegrityLevel, 0, 0, out var needed);
            if (needed == 0) return IntegrityLevel.Unknown;

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!Security.GetTokenInformation(token, Security.TokenIntegrityLevel, buffer, needed, out _))
                    return IntegrityLevel.Unknown;

                var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
                var countPtr = Security.GetSidSubAuthorityCount(label.Label.Sid);
                if (countPtr == 0) return IntegrityLevel.Unknown;

                var count = Marshal.ReadByte(countPtr);
                if (count == 0) return IntegrityLevel.Unknown;

                var ridPtr = Security.GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
                if (ridPtr == 0) return IntegrityLevel.Unknown;

                var rid = (uint)Marshal.ReadInt32(ridPtr);

                return rid switch
                {
                    >= Security.SECURITY_MANDATORY_SYSTEM_RID => IntegrityLevel.System,
                    >= Security.SECURITY_MANDATORY_HIGH_RID => IntegrityLevel.High,
                    >= Security.SECURITY_MANDATORY_MEDIUM_RID => IntegrityLevel.Medium,
                    _ => IntegrityLevel.Low,
                };
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { Security.CloseHandle(token); }
    }
}
