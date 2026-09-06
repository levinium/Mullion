using Mullion.Core.Abstractions;
using Mullion.Core.Geometry;
using Mullion.Core.Model;

namespace Mullion.Core.Simulation;

public sealed record SimulatedTopology(string Id, string Name, string Note, IReadOnlyList<DisplayInfo> Displays);

/// <summary>
/// Fake display arrangements, so the layout engine and the UI can be seen
/// handling setups the machine in front of you does not have.
/// <para>
/// This is not a test fixture. Almost every arrangement Mullion has to cope
/// with is one its author cannot plug in, so without this the multi-monitor
/// paths are only ever verified as numbers in a test - never actually looked at.
/// </para>
/// </summary>
public static class SimulatedTopologies
{
    public static IReadOnlyList<SimulatedTopology> All { get; } =
    [
        new("single-32-9", "Single 32:9 super-ultrawide",
            "5120x1440. The 16:9 center rule applies: 25 / 50 / 25.",
            [D(0, 0, 5120, 1440, primary: true, taskbar: 48, name: "Odyssey CRG9")]),

        new("single-21-9", "Single 21:9 ultrawide",
            "3440x1440. A 16:9 center would need 74% of the width, so this splits in two instead.",
            [D(0, 0, 3440, 1440, primary: true, taskbar: 48, name: "Ultrawide 34")]),

        new("single-16-9", "Single 16:9",
            "2560x1440. One display alone always subdivides - a single whole-screen zone is no tiling at all.",
            [D(0, 0, 2560, 1440, primary: true, taskbar: 48, name: "Dell U2718Q")]),

        new("single-4k", "Single 4K",
            "3840x2160 at 150% scaling. The pixel floor is DPI-scaled, so this behaves like its apparent size.",
            [D(0, 0, 3840, 2160, dpi: 144, primary: true, taskbar: 72, name: "4K 27in")]),

        new("two-across", "Two monitors side by side",
            "A and D, with S bound to the span across both since they form a clean rectangle.",
            [
                D(0, 0, 2560, 1440, primary: true, taskbar: 48, name: "Left 27in"),
                D(2560, 0, 2560, 1440, taskbar: 48, name: "Right 27in"),
            ]),

        new("three-across", "Three monitors side by side",
            "The original three-monitor case: A / S / D, with halves above and below each.",
            [
                D(-2560, 0, 2560, 1440, taskbar: 48, name: "Left"),
                D(0, 0, 2560, 1440, primary: true, taskbar: 48, name: "Center"),
                D(2560, 0, 2560, 1440, taskbar: 48, name: "Right"),
            ]),

        new("three-portrait", "Three portrait monitors",
            "Nine zones from nine keys: every column has a row to spare, so each gets upper / whole / lower.",
            [
                D(-1080, 0, 1080, 1920, taskbar: 48, name: "Left portrait"),
                D(0, 0, 1080, 1920, primary: true, taskbar: 48, name: "Center portrait"),
                D(1080, 0, 1080, 1920, taskbar: 48, name: "Right portrait"),
            ]),

        new("standard-plus-ultrawide", "16:9 beside a 32:9",
            "Columns are allocated by demand, not evenly: 1 + 3, not 2 + 2.",
            [
                D(-2560, 0, 2560, 1440, taskbar: 48, name: "Side 27in"),
                D(0, 0, 5120, 1440, primary: true, taskbar: 48, name: "Odyssey CRG9"),
            ]),

        new("verticals-flanking-stacked", "Two verticals flanking a stacked pair",
            "The arrangement that breaks global band clustering - each vertical overlaps both center monitors.",
            [
                D(-1080, 0, 1080, 2160, taskbar: 48, name: "Left portrait"),
                D(0, 0, 1920, 1080, name: "Center upper"),
                D(0, 1080, 1920, 1080, primary: true, taskbar: 48, name: "Center lower"),
                D(1920, 0, 1080, 2160, taskbar: 48, name: "Right portrait"),
            ]),

        new("two-stacked", "Two monitors stacked",
            "One column, two displays: the outer rows take the monitors and the home row spans both.",
            [
                D(0, 0, 2560, 1440, name: "Top"),
                D(0, 1440, 2560, 1440, primary: true, taskbar: 48, name: "Bottom"),
            ]),

        new("two-by-two", "Four monitors in a 2x2 grid",
            "Two columns, two deep.",
            [
                D(0, 0, 1920, 1080, name: "Top left"),
                D(1920, 0, 1920, 1080, name: "Top right"),
                D(0, 1080, 1920, 1080, primary: true, taskbar: 48, name: "Bottom left"),
                D(1920, 1080, 1920, 1080, taskbar: 48, name: "Bottom right"),
            ]),

        new("laptop-plus-external", "Laptop and an external, mixed DPI",
            "150% laptop panel beside a 100% external. Differing scaling blocks any spanning zone.",
            [
                D(0, 0, 2560, 1440, primary: true, taskbar: 48, name: "External 27in"),
                D(2560, 300, 2256, 1504, dpi: 144, taskbar: 72, name: "Laptop 3:2"),
            ]),

        new("rotated-32-9", "A 32:9 rotated to portrait",
            "1440x5120. Still a 32:9, so it splits into three STACKED zones, not a generic tall layout.",
            [D(0, 0, 1440, 5120, primary: true, taskbar: 48, name: "CRG9 rotated")]),

        new("l-shape", "L-shape: two across, one above the right",
            "Uneven column depths - the right column is two deep, the left one.",
            [
                D(0, 300, 1920, 1080, primary: true, taskbar: 48, name: "Left"),
                D(1920, 0, 1920, 1080, name: "Right upper"),
                D(1920, 1080, 1920, 1080, taskbar: 48, name: "Right lower"),
            ]),

        new("ancient-and-modern", "A 5:4 beside a 16:9",
            "An aspect ratio no table anticipated. The continuous shape formula handles it without a special case.",
            [
                D(0, 200, 1280, 1024, taskbar: 40, name: "5:4 legacy"),
                D(1280, 0, 2560, 1440, primary: true, taskbar: 48, name: "Modern 27in"),
            ]),
    ];

    public static SimulatedTopology? Find(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    private static DisplayInfo D(
        int x, int y, int w, int h,
        uint dpi = 96, bool primary = false, int taskbar = 0, string name = "Display")
    {
        var bounds = new PxRect(x, y, w, h);

        return new DisplayInfo
        {
            // A stable key derived from the geometry, so a simulated profile is
            // distinct from the real machine's and cannot collide with it.
            StableKey = $"SIM-{name.Replace(" ", string.Empty)}-{w}x{h}",
            GdiDeviceName = $@"\\.\SIMULATED{Math.Abs(x) + Math.Abs(y)}",
            FriendlyName = name,
            Bounds = bounds,
            WorkArea = taskbar > 0 ? bounds.Deflate(0, 0, 0, taskbar) : bounds,
            Dpi = dpi,
            IsPrimary = primary,
        };
    }
}

/// <summary>Serves a fixed arrangement in place of the real hardware.</summary>
public sealed class SimulatedDisplayProvider(SimulatedTopology topology) : IDisplayProvider
{
    public SimulatedTopology Topology { get; } = topology;

    public IReadOnlyList<DisplayInfo> GetDisplays() => Topology.Displays;

    public IReadOnlyList<string> Diagnostics => [$"Simulated arrangement: {Topology.Name}."];
}
