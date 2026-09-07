using Avalonia;
using Mullion.Core.Hotkeys;
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
    string? BlockedByWindow,

    /// <summary>Name of the simulated arrangement, or null when using real hardware.</summary>
    string? SimulationName,

    /// <summary>Another zone manager is reacting to the same drag gesture.</summary>
    bool DragConflict,

    string? DragConflictDetail,

    /// <summary>Label for the conflict banner's button; what is left to do changes.</summary>
    string DragConflictAction);

/// <summary>
/// Everything the UI needs from the platform, behind one interface so the
/// views and view models never reference Mullion.Platform.Windows directly.
/// That boundary is what keeps a macOS backend a self-contained job.
/// </summary>
public interface IAppHost : IZoneEditingHost
{
    event Action? StateChanged;

    bool Paused { get; set; }

    /// <summary>
    /// Whether to open straight to the tray. Read before the window is shown,
    /// so it cannot come from the UI snapshot.
    /// </summary>
    bool StartInTray { get; }

    void RestartElevated();

    /// <summary>Turn off the other zone manager competing for the drag gesture.</summary>
    void ResolveDragConflict();

    AppSnapshot GetSnapshot();

    void Rescan();

    void Start();
}

/// <summary>Sample data so the XAML previewer and non-Windows builds have something to draw.</summary>
public sealed class DesignAppHost : IAppHost
{
    public event Action? StateChanged;

    public bool Paused { get; set; }

    public bool StartInTray => false;

    public void Start() => StateChanged?.Invoke();

    public void RestartElevated() { }

    public void ResolveDragConflict() { }

    public void Rescan() => StateChanged?.Invoke();

    // Editing does nothing here: this host exists so the XAML previewer and
    // non-Windows builds have something to draw, and there is no config behind
    // it to change.
    public MonitorDiagramViewModel BuildInteractiveDiagram(
        Action<GridPos>? onZoneActivated,
        Action<string, IReadOnlyList<double>>? onSplitChanged,
        Action<string, int>? onZoneCountChanged) => GetSnapshot().Diagram;

    public void SetDisplayColumns(string slot, int columns) { }
    public void SetDisplayWeights(string slot, IReadOnlyList<double> weights) { }
    public bool HasCustomZones => false;
    public void ResetAllOverrides() { }
    public void ResetLayout() { }
    public bool CanUndoZones => false;
    public bool CanRedoZones => false;
    public void UndoZones() { }
    public void RedoZones() { }
    public bool SnapSplits => true;
    public void SetSnapSplits(bool value) { }
    public void BeginRebind(int row, int col, Action<RebindResult> completed) =>
        completed(new RebindResult(false, "Not available here."));
    public void CancelRebind() { }

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
                    Slot = "5120x1440@0,0",
                    Title = "Sample display",
                    Detail = "5120 × 1440 · primary",
                    Bounds = new Rect(0, 0, 5120, 1440),
                    IsPrimary = true,
                    HasTaskbar = true,
                    LabelAbove = true,
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
            null,
            null,
            false,
            null,
            "Turn off FancyZones");
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
