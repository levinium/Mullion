using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mullion.Core.Geometry;
using Mullion.Core.Model;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Displays;

/// <summary>
/// Enumerates displays via EnumDisplayMonitors for geometry, joined to
/// QueryDisplayConfig for EDID-derived stable identity through the
/// GDI device name the two APIs share.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayProvider : Mullion.Core.Abstractions.IDisplayProvider
{
    /// <summary>
    /// Must run before any UI framework initializes. The manifest also declares
    /// PerMonitorV2; this is belt and braces, because without it every coordinate
    /// we read is virtualised and the zone maths silently operates on lies.
    /// </summary>
    public static void EnsurePerMonitorDpiAwareness()
    {
        try
        {
            User32.SetProcessDpiAwarenessContext(User32.DpiAwarenessContextPerMonitorAwareV2);
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1703. The manifest still applies.
        }
    }

    /// <summary>
    /// Which identity tier each display resolved to, and why, for the
    /// Diagnostics page. Falling back to a GDI name means profiles will thrash
    /// when displays are renumbered, so it needs to be visible, not silent.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; private set; } = [];

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var diagnostics = new List<string>();
        var identities = ReadIdentities(diagnostics);
        Diagnostics = diagnostics;
        var monitors = new List<DisplayInfo>();
        var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);

        User32.MonitorEnumProc callback = (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() };
            if (!User32.GetMonitorInfo(hMonitor, ref info)) return 1;

            string gdiName;
            unsafe { gdiName = new string(info.szDevice); }
            gdiName = gdiName.TrimEnd('\0');

            var dpi = 96u;
            if (Shcore.GetDpiForMonitor(hMonitor, Shcore.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                dpi = dpiX;

            identities.TryGetValue(gdiName, out var identity);

            // Disambiguate identical models deterministically, so two of the same
            // panel do not collapse onto one key.
            var baseKey = identity.StableKey ?? Sanitise(gdiName);
            var key = baseKey;
            if (seenKeys.TryGetValue(baseKey, out var n))
            {
                key = $"{baseKey}#{n + 1}";
                seenKeys[baseKey] = n + 1;
            }
            else
            {
                seenKeys[baseKey] = 1;
            }

            monitors.Add(new DisplayInfo
            {
                StableKey = key,
                GdiDeviceName = gdiName,
                FriendlyName = string.IsNullOrWhiteSpace(identity.FriendlyName)
                    ? "Display"
                    : identity.FriendlyName,
                Bounds = ToPxRect(info.rcMonitor),
                WorkArea = ToPxRect(info.rcWork),
                Dpi = dpi,
                IsPrimary = (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0,
                Rotation = identity.Rotation,
                Handle = hMonitor,
            });

            return 1;
        };

        User32.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        return monitors;
    }

    private readonly record struct Identity(string? StableKey, string? FriendlyName, Rotation Rotation);

    /// <summary>
    /// Map GDI device name -> EDID identity. Falls back gracefully: some virtual
    /// and remote adapters (RDP, Parsec, virtual display drivers) return nothing
    /// from QueryDisplayConfig at all.
    /// </summary>
    private static Dictionary<string, Identity> ReadIdentities(List<string> diagnostics)
    {
        var result = new Dictionary<string, Identity>(StringComparer.OrdinalIgnoreCase);

        try
        {
            DisplayConfig.AssertLayout();

            var sizeResult = DisplayConfig.GetDisplayConfigBufferSizes(
                DisplayConfig.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);

            if (sizeResult != DisplayConfig.ERROR_SUCCESS)
            {
                diagnostics.Add($"GetDisplayConfigBufferSizes failed ({sizeResult}); using GDI names.");
                return result;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            var queryResult = DisplayConfig.QueryDisplayConfig(
                DisplayConfig.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount, paths, ref modeCount, modes, 0);

            if (queryResult != DisplayConfig.ERROR_SUCCESS)
            {
                diagnostics.Add($"QueryDisplayConfig failed ({queryResult}); using GDI names.");
                return result;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DisplayConfig.DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id,
                    },
                };

                var sourceResult = DisplayConfig.DisplayConfigGetDeviceInfo(ref source);
                if (sourceResult != DisplayConfig.ERROR_SUCCESS)
                {
                    diagnostics.Add($"Path {i}: source name lookup failed ({sourceResult}).");
                    continue;
                }

                string gdiName;
                unsafe { gdiName = new string(source.viewGdiDeviceName).TrimEnd('\0'); }
                if (string.IsNullOrEmpty(gdiName)) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DisplayConfig.DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id,
                    },
                };

                string? stableKey = null;
                string? friendly = null;

                var targetResult = DisplayConfig.DisplayConfigGetDeviceInfo(ref target);
                if (targetResult == DisplayConfig.ERROR_SUCCESS)
                {
                    unsafe { friendly = new string(target.monitorFriendlyDeviceName).TrimEnd('\0'); }

                    if (target.edidManufactureId != 0)
                    {
                        stableKey = $"{DecodePnpId(target.edidManufactureId)}-{target.edidProductCodeId:X4}";
                        diagnostics.Add($"{gdiName}: EDID identity {stableKey} ({friendly}).");
                    }
                    else
                    {
                        diagnostics.Add($"{gdiName}: no EDID manufacturer id; using GDI name.");
                    }
                }
                else
                {
                    diagnostics.Add($"{gdiName}: target name lookup failed ({targetResult}); using GDI name.");
                }

                result[gdiName] = new Identity(stableKey, friendly, DecodeRotation(path.targetInfo.rotation));
            }
        }
        catch (DllNotFoundException e)
        {
            diagnostics.Add($"QueryDisplayConfig unavailable ({e.Message}); using GDI names.");
        }
        catch (EntryPointNotFoundException e)
        {
            diagnostics.Add($"QueryDisplayConfig unavailable ({e.Message}); using GDI names.");
        }
        catch (InvalidOperationException e)
        {
            diagnostics.Add($"Struct layout mismatch: {e.Message}");
        }

        return result;
    }

    /// <summary>EDID packs three 5-bit letters into a big-endian ushort.</summary>
    private static string DecodePnpId(ushort id)
    {
        var v = (ushort)((id >> 8) | (id << 8));   // stored big-endian
        Span<char> chars =
        [
            (char)('A' + ((v >> 10) & 0x1F) - 1),
            (char)('A' + ((v >> 5) & 0x1F) - 1),
            (char)('A' + (v & 0x1F) - 1),
        ];

        foreach (var c in chars)
            if (c is < 'A' or > 'Z') return "UNK";

        return new string(chars);
    }

    private static Rotation DecodeRotation(uint r) => r switch
    {
        2 => Rotation.Cw90,
        3 => Rotation.Cw180,
        4 => Rotation.Cw270,
        _ => Rotation.None,
    };

    private static string Sanitise(string gdiName) =>
        gdiName.Replace(@"\\.\", string.Empty, StringComparison.Ordinal)
               .Replace(@"\", string.Empty, StringComparison.Ordinal);

    private static PxRect ToPxRect(RECT r) => PxRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom);
}
