using Mullion.Core.Geometry;
using Mullion.Core.Model;

namespace Mullion.Core.Tests;

/// <summary>
/// Synthetic topologies. Most of the arrangements this app has to handle cannot
/// be reproduced on the development machine (a single 5120x1440), so these
/// fixtures are the primary verification surface, not a convenience.
/// </summary>
public static class TestDisplays
{
    private static int _seq;

    public static DisplayInfo At(
        int x, int y, int w, int h,
        uint dpi = 96,
        bool primary = false,
        int taskbar = 0,
        string? key = null)
    {
        var bounds = new PxRect(x, y, w, h);
        return new DisplayInfo
        {
            StableKey = key ?? $"TEST-{++_seq:X4}",
            GdiDeviceName = $@"\\.\DISPLAY{_seq}",
            FriendlyName = $"Test {w}x{h}",
            Bounds = bounds,
            WorkArea = taskbar > 0 ? bounds.Deflate(0, 0, 0, taskbar) : bounds,
            Dpi = dpi,
            IsPrimary = primary,
        };
    }

    /// <summary>The development machine: one 5120x1440 with a 48px taskbar.</summary>
    public static List<DisplayInfo> SuperUltrawideAlone() =>
        [At(0, 0, 5120, 1440, primary: true, taskbar: 48, key: "DEV-3209")];

    /// <summary>Three landscape monitors side by side.</summary>
    public static List<DisplayInfo> ThreeAcross() =>
    [
        At(-2560, 0, 2560, 1440, key: "L"),
        At(0, 0, 2560, 1440, primary: true, key: "C"),
        At(2560, 0, 2560, 1440, key: "R"),
    ];

    /// <summary>Two landscape monitors, side by side.</summary>
    public static List<DisplayInfo> TwoAcross() =>
    [
        At(0, 0, 2560, 1440, primary: true, key: "L"),
        At(2560, 0, 2560, 1440, key: "R"),
    ];

    /// <summary>Three portrait monitors side by side - the nine-key case.</summary>
    public static List<DisplayInfo> ThreePortraitAcross() =>
    [
        At(-1080, 0, 1080, 1920, key: "L"),
        At(0, 0, 1080, 1920, primary: true, key: "C"),
        At(1080, 0, 1080, 1920, key: "R"),
    ];

    /// <summary>
    /// The arrangement that broke global band clustering: two verticals flanking
    /// a stacked pair. Each vertical overlaps BOTH centre monitors vertically.
    /// </summary>
    public static List<DisplayInfo> VerticalsFlankingStackedPair() =>
    [
        At(-1080, 0, 1080, 2160, key: "LV"),
        At(0, 0, 1920, 1080, key: "CU"),
        At(0, 1080, 1920, 1080, primary: true, key: "CL"),
        At(1920, 0, 1080, 2160, key: "RV"),
    ];

    /// <summary>Two verticals flanking a single landscape display - all one deep.</summary>
    public static List<DisplayInfo> VerticalsFlankingOneLandscape() =>
    [
        At(-1080, 0, 1080, 2160, key: "LV"),
        At(0, 540, 2560, 1440, primary: true, key: "C"),
        At(2560, 0, 1080, 2160, key: "RV"),
    ];

    /// <summary>A standard monitor beside a 32:9 - the "4-way" case from the brief.</summary>
    public static List<DisplayInfo> StandardPlusSuperUltrawide() =>
    [
        At(-2560, 0, 2560, 1440, key: "STD"),
        At(0, 0, 5120, 1440, primary: true, key: "UW"),
    ];

    /// <summary>Two monitors stacked vertically.</summary>
    public static List<DisplayInfo> TwoStacked() =>
    [
        At(0, 0, 2560, 1440, key: "TOP"),
        At(0, 1440, 2560, 1440, primary: true, key: "BOTTOM"),
    ];

    /// <summary>A 2x2 grid of four monitors.</summary>
    public static List<DisplayInfo> TwoByTwo() =>
    [
        At(0, 0, 1920, 1080, key: "TL"),
        At(1920, 0, 1920, 1080, key: "TR"),
        At(0, 1080, 1920, 1080, primary: true, key: "BL"),
        At(1920, 1080, 1920, 1080, key: "BR"),
    ];

    /// <summary>Mixed DPI: a 150%-scaled laptop panel beside a 100% external.</summary>
    public static List<DisplayInfo> MixedDpi() =>
    [
        At(0, 0, 2560, 1440, dpi: 96, primary: true, key: "EXT"),
        At(2560, 0, 2256, 1504, dpi: 144, key: "LAPTOP"),
    ];
}
