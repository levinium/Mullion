using System.Runtime.InteropServices;

namespace Mullion.Platform.Windows.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct MONITORINFOEXW
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    public fixed char szDevice[32];
}

internal static partial class User32
{
    internal const uint MONITORINFOF_PRIMARY = 0x1;

    internal delegate int MonitorEnumProc(nint hMonitor, nint hdc, nint lprcClip, nint dwData);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(
        nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEXW lpmi);

    [LibraryImport("user32.dll")]
    internal static partial uint SetProcessDpiAwarenessContext(nint value);

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.</summary>
    internal static readonly nint DpiAwarenessContextPerMonitorAwareV2 = -4;
}

internal static partial class Shcore
{
    internal const int MDT_EFFECTIVE_DPI = 0;

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(
        nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
