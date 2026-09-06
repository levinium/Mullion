using Avalonia;
using Mullion.App.ViewModels;

namespace Mullion.App.Services;

public sealed record AppSnapshot(
    MonitorDiagramViewModel Diagram,
    string TopologySummary,
    string HookStatus,
    string PrivilegeStatus,
    bool Paused,
    string LastAction,
    IReadOnlyList<ConflictViewModel> Conflicts,
    bool IsElevated,

    /// <summary>Title of the elevated window currently blocking hotkeys, or null.</summary>
    string? BlockedByWindow);

/// <summary>
/// Everything the UI needs from the platform, behind one interface so the
/// views and view models never reference Mullion.Platform.Windows directly.
/// That boundary is what keeps a macOS backend a self-contained job.
/// </summary>
public interface IAppHost
{
    event Action? StateChanged;

    bool Paused { get; set; }

    void RestartElevated();

    AppSnapshot GetSnapshot();

    void Rescan();

    void Start();
}

/// <summary>Sample data so the XAML previewer and non-Windows builds have something to draw.</summary>
public sealed class DesignAppHost : IAppHost
{
    public event Action? StateChanged;

    public bool Paused { get; set; }

    public void Start() => StateChanged?.Invoke();

    public void RestartElevated() { }

    public void Rescan() => StateChanged?.Invoke();

    public AppSnapshot GetSnapshot()
    {
        var diagram = new MonitorDiagramViewModel
        {
            VirtualBounds = new Rect(0, 0, 5120, 1440),
            Displays =
            [
                new DisplayNodeViewModel
                {
                    Key = "SAMPLE",
                    Title = "Sample display",
                    Detail = "5120 × 1440 · primary",
                    Bounds = new Rect(0, 0, 5120, 1440),
                    IsPrimary = true,
                    HasTaskbar = true,
                    TaskbarArea = new Rect(0, 1 - 48.0 / 1440, 1, 48.0 / 1440),
                    Cells =
                    [
                        Cell("Left", "A", 0, 0, 0.25, 1, "Q", "Z"),
                        Cell("Center", "S", 0.25, 0, 0.5, 1, "W", "X"),
                        Cell("Right", "D", 0.75, 0, 0.25, 1, "E", "C"),
                    ],
                },
            ],
        };

        return new AppSnapshot(
            diagram,
            "1 display · 5120 × 1440",
            "Design mode",
            "Not elevated",
            Paused,
            "No hotkey pressed yet.",
            [],
            false,
            null);
    }

    private static ZoneCellViewModel Cell(
        string name, string key, double x, double y, double w, double h, string upper, string lower) =>
        new()
        {
            Name = name,
            KeyLabel = key,
            Area = new Rect(x, y, w, h),
            SizeLabel = "—",
            SpansDisplays = false,
            UpperKey = upper,
            UpperSize = "—",
            LowerKey = lower,
            LowerSize = "—",
        };
}
