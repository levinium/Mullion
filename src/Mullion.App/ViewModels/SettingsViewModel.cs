using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;
using Mullion.Core.Abstractions;
using Mullion.Core.Hotkeys;

namespace Mullion.App.ViewModels;

public sealed partial class BindingRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private bool _isCapturing;

    public required string Zone { get; init; }
    public required int Row { get; init; }
    public required int Col { get; init; }

    public string ButtonLabel => IsCapturing ? "Press a key…" : "Change";

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(ButtonLabel));
}

public sealed record SurfaceOption(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record SuppressionOption(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private bool _loading;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startElevated;

    [ObservableProperty]
    private bool _showZoneFlash;

    [ObservableProperty]
    private bool _allowSpanningUnions;

    [ObservableProperty]
    private bool _startInTray;

    /// <summary>Win is the default and the reason for the hook; the rest leave it alone.</summary>
    public IReadOnlyList<string> HotkeyModifiers { get; } = ModifierChoice.All;

    [ObservableProperty]
    private string _selectedHotkeyModifier = ModifierChoice.Default;

    [ObservableProperty]
    private bool _dragToSnap;

    /// <summary>"None" means always armed; the rest name a modifier to hold.</summary>
    public IReadOnlyList<string> DragModifiers { get; } =
        ["Shift", "Control", "Alt", "Win", "None"];

    [ObservableProperty]
    private string _selectedDragModifier = "Shift";

    [ObservableProperty]
    private string _winKeySuppression = "DummyKey";

    [ObservableProperty]
    private SuppressionOption? _selectedSuppression;

    [ObservableProperty]
    private SurfaceOption? _selectedSurface;

    [ObservableProperty]
    private IReadOnlyList<SurfaceOption> _surfaces = [];

    [ObservableProperty]
    private IReadOnlyList<BindingRowViewModel> _bindings = [];

    [ObservableProperty]
    private string? _rebindMessage;

    /// <summary>
    /// The same diagram the main window shows, but clickable: picking a zone is
    /// how you rebind it. Reusing the picture beats listing the same information
    /// again in a table where the spatial arrangement is invisible.
    /// </summary>
    [ObservableProperty]
    private MonitorDiagramViewModel _diagram = new();

    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _logPath = string.Empty;

    /// <summary>Includes the commit, so a bug report identifies the exact build.</summary>
    public string VersionLine => $"Mullion {Services.BuildInfo.Full}";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isElevated;

    public SettingsViewModel() : this(new DesignSettingsHost()) { }

    public SettingsViewModel(ISettingsHost host)
    {
        _host = host;
        Reload();
    }

    /// <summary>
    /// Labelled rather than raw enum names. "None" means the Start menu opens on
    /// every hotkey - the app's most visible failure - so it has to say so
    /// rather than looking like a neutral third choice.
    /// </summary>
    public IReadOnlyList<SuppressionOption> SuppressionModes { get; } =
    [
        new("DummyKey", "Suppress with a dummy keypress (recommended)"),
        new("SwallowKeyUp", "Swallow the Windows key release"),
        new("None", "Don't suppress — the Start menu will open (diagnostic only)"),
    ];

    public bool SuppressionIsDefault => WinKeySuppression == "DummyKey";

    public string SuppressionWarning => WinKeySuppression switch
    {
        "None" => "The Start menu will open every time you use a Mullion hotkey. "
                  + "Set this back to the recommended option unless you are diagnosing a problem.",
        "SwallowKeyUp" => "The Windows key release is swallowed entirely. Use this only if the "
                          + "recommended option fails to stop the Start menu appearing.",
        _ => string.Empty,
    };

    public bool HasError => Error is not null;

    public string ElevationBlurb => IsElevated
        ? "Running as administrator — windows that run elevated can be managed."
        : "Not running as administrator. Hotkeys will not fire while a window running as "
          + "administrator has focus, and such windows cannot be moved.";

    public void Reload()
    {
        _loading = true;

        var s = _host.GetSettings();

        StartWithWindows = s.AutoStart is AutoStartMode.Standard or AutoStartMode.Elevated;
        StartElevated = s.AutoStart is AutoStartMode.Elevated;
        ShowZoneFlash = s.ShowZoneFlash;
        AllowSpanningUnions = s.AllowSpanningUnions;
        StartInTray = s.StartInTray;
        SelectedHotkeyModifier = HotkeyModifiers.FirstOrDefault(
            x => string.Equals(x, s.HotkeyModifier, StringComparison.OrdinalIgnoreCase))
            ?? ModifierChoice.Default;
        DragToSnap = s.DragToSnap;
        SelectedDragModifier = DragModifiers.FirstOrDefault(
            x => string.Equals(x, s.DragModifier, StringComparison.OrdinalIgnoreCase)) ?? "Shift";
        WinKeySuppression = s.WinKeySuppression;
        SelectedSuppression = SuppressionModes.FirstOrDefault(x => x.Id == s.WinKeySuppression)
                              ?? SuppressionModes[0];
        IsElevated = s.IsElevated;
        ConfigPath = s.ConfigPath;
        LogPath = s.LogPath;

        Surfaces = [.. s.AvailableSurfaces.Select(x => new SurfaceOption(x.Id, x.Name))];
        SelectedSurface = Surfaces.FirstOrDefault(x => x.Id == s.SurfaceId);
        Bindings = [.. s.Bindings.Select(b => new BindingRowViewModel
        {
            Key = b.Key,
            Zone = b.Zone,
            Row = b.Row,
            Col = b.Col,
        })];

        Diagram = _host.BuildInteractiveDiagram(BeginRebindAt);

        OnPropertyChanged(nameof(ElevationBlurb));
        _loading = false;
    }

    partial void OnStartWithWindowsChanged(bool value) => ApplyAutoStart();

    partial void OnStartElevatedChanged(bool value) => ApplyAutoStart();

    partial void OnShowZoneFlashChanged(bool value)
    {
        if (!_loading) _host.SetShowZoneFlash(value);
    }

    partial void OnSelectedHotkeyModifierChanged(string value)
    {
        if (!_loading) _host.SetHotkeyModifier(value);
    }

    partial void OnStartInTrayChanged(bool value)
    {
        if (!_loading) _host.SetStartInTray(value);
    }

    partial void OnDragToSnapChanged(bool value)
    {
        if (!_loading) _host.SetDragToSnap(value, SelectedDragModifier);
    }

    partial void OnSelectedDragModifierChanged(string value)
    {
        if (!_loading) _host.SetDragToSnap(DragToSnap, value);
    }

    partial void OnAllowSpanningUnionsChanged(bool value)
    {
        if (!_loading) _host.SetAllowSpanningUnions(value);
        if (!_loading) Reload();
    }

    partial void OnSelectedSuppressionChanged(SuppressionOption? value)
    {
        if (value is null) return;

        WinKeySuppression = value.Id;
        if (!_loading) _host.SetWinKeySuppression(value.Id);

        OnPropertyChanged(nameof(SuppressionIsDefault));
        OnPropertyChanged(nameof(SuppressionWarning));
    }

    [RelayCommand]
    private void ResetSuppression() =>
        SelectedSuppression = SuppressionModes[0];

    partial void OnSelectedSurfaceChanged(SurfaceOption? value)
    {
        if (_loading || value is null) return;

        _host.SetKeySurface(value.Id);
        Reload();
    }

    private void ApplyAutoStart()
    {
        if (_loading) return;

        var mode = !StartWithWindows
            ? AutoStartMode.Disabled
            : StartElevated ? AutoStartMode.Elevated : AutoStartMode.Standard;

        SetError(_host.SetAutoStart(mode));

        // Registering elevated auto-start can fail (it needs one elevated run),
        // so read back what actually took effect rather than assuming.
        Reload();
    }

    private void SetError(string? message)
    {
        Error = message;
        OnPropertyChanged(nameof(HasError));
    }

    private GridPos? _capturing;

    [RelayCommand]
    private void Rebind(BindingRowViewModel? row)
    {
        if (row is not null) BeginRebindAt(new GridPos(row.Row, row.Col));
    }

    /// <summary>Start capture for a zone, from the diagram or the list alike.</summary>
    private void BeginRebindAt(GridPos position)
    {
        // Clicking the same zone again cancels, so capture is never a trap the
        // user cannot get out of.
        if (_capturing == position)
        {
            CancelCapture();
            return;
        }

        CancelCapture();

        _capturing = position;
        Diagram.SetCapturing(position);
        SetRowCapturing(position, true);

        RebindMessage = "Hold Win and press the key you want for this zone. Click it again to cancel.";

        _host.BeginRebind(position.Row, position.Col, result =>
        {
            SetRowCapturing(position, false);
            Diagram.SetCapturing(null);
            _capturing = null;
            RebindMessage = result.Message;

            if (result.Success) Reload();
        });
    }

    private void SetRowCapturing(GridPos position, bool capturing)
    {
        foreach (var row in Bindings)
            if (row.Row == position.Row && row.Col == position.Col)
                row.IsCapturing = capturing;
    }

    private void CancelCapture()
    {
        if (_capturing is null) return;

        SetRowCapturing(_capturing.Value, false);
        Diagram.SetCapturing(null);
        _capturing = null;
        _host.CancelRebind();
        RebindMessage = null;
    }

    [RelayCommand]
    private void ResetLayout()
    {
        _host.ResetLayout();
        RebindMessage = "Layout reset to the generated default.";
        Reload();
    }

    [RelayCommand]
    private void RestartElevated() => _host.RestartElevated();

    [RelayCommand]
    private void OpenConfigFolder() => _host.OpenConfigFolder();

    [RelayCommand]
    private void OpenLogFolder() => _host.OpenLogFolder();

    [RelayCommand]
    private void RerunWizard() => _host.RerunWizard();
}

public sealed class DesignSettingsHost : ISettingsHost
{
    public SettingsSnapshot GetSettings() => new(
        AutoStartMode.Disabled, false, false, true, true, "DummyKey",
        "left-hand",
        [("left-hand", "Left hand (QWERT / ASDFG / ZXCVB)")],
        [
            new BindingEntry("Win+A", "Left", 1, 0),
            new BindingEntry("Win+S", "Center", 1, 1),
            new BindingEntry("Win+D", "Right", 1, 2),
        ],
        @"%APPDATA%\Mullion\config.json",
        @"%LOCALAPPDATA%\Mullion\logs\mullion.log",
        true,
        "Shift",
        false,
        "Win");

    public string? SetAutoStart(AutoStartMode mode) => null;
    public void BeginRebind(int row, int col, Action<RebindResult> completed) { }
    public void CancelRebind() { }
    public void ResetLayout() { }
    public MonitorDiagramViewModel BuildInteractiveDiagram(Action<GridPos> onZoneActivated) => new();
    public void SetShowZoneFlash(bool value) { }
    public void SetAllowSpanningUnions(bool value) { }

    public void SetDragToSnap(bool enabled, string modifier) { }

    public void SetStartInTray(bool value) { }

    public void SetHotkeyModifier(string value) { }
    public void SetWinKeySuppression(string value) { }
    public void SetKeySurface(string surfaceId) { }
    public void RestartElevated() { }
    public void OpenConfigFolder() { }
    public void OpenLogFolder() { }
    public void RerunWizard() { }
}
