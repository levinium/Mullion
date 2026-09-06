using System.Runtime.InteropServices;

namespace Mullion.Platform.Windows.Native;

// QueryDisplayConfig gives EDID-derived identity, which is what makes a display
// key stable. The obvious alternatives are both wrong: \\.\DISPLAY1 is renumbered
// by the OS, and monitorDevicePath changes when a monitor is replugged into a
// different port.

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint outputTechnology;
    public uint rotation;
    public uint scaling;

    // DISPLAYCONFIG_RATIONAL is two UINT32s, NOT a ulong. Declaring it as ulong
    // gives the field 8-byte alignment, which pads this struct from 48 to 56
    // bytes and shifts every subsequent field - the target adapterId and id then
    // read from the wrong offsets and DisplayConfigGetDeviceInfo returns
    // ERROR_INVALID_PARAMETER while the struct's own size still looks correct.
    public uint refreshRateNumerator;
    public uint refreshRateDenominator;

    public uint scanLineOrdering;

    // int, not bool: LibraryImport marshals arrays of structs only when the
    // struct is blittable, and a MarshalAs'd bool makes it non-blittable.
    public int targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_INFO
{
    public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
    public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_MODE_INFO_BLOB
{
    public ulong a;
    public ulong b;
    public ulong c;
    public ulong d;
    public ulong e;
    public ulong f;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_MODE_INFO
{
    public uint infoType;
    public uint id;
    public LUID adapterId;
    public DISPLAYCONFIG_MODE_INFO_BLOB blob;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public uint type;
    public uint size;
    public LUID adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct DISPLAYCONFIG_TARGET_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint flags;
    public uint outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;
    public fixed char monitorFriendlyDeviceName[64];
    public fixed char monitorDevicePath[128];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public fixed char viewGdiDeviceName[32];
}

internal static partial class DisplayConfig
{
    /// <summary>
    /// Native sizes these structs must match exactly. A mismatch does not throw:
    /// the API returns ERROR_INVALID_PARAMETER and identity silently degrades to
    /// volatile GDI names, so it is worth asserting up front.
    /// </summary>
    internal static void AssertLayout()
    {
        Check<DISPLAYCONFIG_DEVICE_INFO_HEADER>(20);
        Check<DISPLAYCONFIG_PATH_SOURCE_INFO>(20);
        Check<DISPLAYCONFIG_PATH_TARGET_INFO>(48);
        Check<DISPLAYCONFIG_PATH_INFO>(72);
        Check<DISPLAYCONFIG_MODE_INFO>(64);
        Check<DISPLAYCONFIG_TARGET_DEVICE_NAME>(420);
        Check<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(84);

        static void Check<T>(int expected) where T : struct
        {
            var actual = Marshal.SizeOf<T>();
            if (actual != expected)
                throw new InvalidOperationException(
                    $"{typeof(T).Name} marshals to {actual} bytes, expected {expected}.");
        }
    }

    internal const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    internal const uint DEVICE_INFO_GET_TARGET_NAME = 2;
    internal const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
    internal const int ERROR_SUCCESS = 0;

    [LibraryImport("user32.dll")]
    internal static partial int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport("user32.dll")]
    internal static partial int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        nint currentTopologyId);

    [LibraryImport("user32.dll")]
    internal static partial int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [LibraryImport("user32.dll")]
    internal static partial int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);
}
