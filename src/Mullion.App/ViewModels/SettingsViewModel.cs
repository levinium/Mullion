using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;
using Mullion.Core.Abstractions;
using Mullion.Core.Hotkeys;

namespace Mullion.App.ViewModels;

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
    [ObservableProperty]
    private string? _layoutMessage;

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


    /// <summary>
    /// The same diagram the main window shows, but clickable: picking a zone is
    /// how you rebind it. Reusing the picture beats listing the same information
    /// again in a table where the spatial arrangement is invisible.
    /// </summary>
    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _logPath = string.Empty;

    /// <summary>Includes the commit, so a bug report identifies the exact build.</summary>
    public string VersionLine => $"Mullion {Services.BuildInfo.Full}";

    // ---- updates -----------------------------------------------------------

    [ObservableProperty]
    private bool _checkForUpdates = true;

    /// <summary>Whether this build was published with anywhere to look.</summary>
    public bool CanCheckForUpdates => Services.UpdateService.IsAvailable;

    /// <summary>What the last check concluded, or null before one has run.</summary>
    [ObservableProperty]
    private string? _updateMessage;

    /// <summary>Set only when there is a newer release, which is what reveals the button.</summary>
    [ObservableProperty]
    private string? _updateUrl;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [RelayCommand]
    private async Task CheckForUpdatesNow()
    {
        if (IsCheckingForUpdates) return;

        IsCheckingForUpdates = true;
        UpdateMessage = "Checking…";
        UpdateUrl = null;

        try
        {
            var verdict = await _host.CheckForUpdatesNow();

            UpdateUrl = verdict.IsAvailable ? verdict.Url ?? Services.UpdateService.PageUrl : null;

            UpdateMessage = verdict.Outcome switch
            {
                Core.Updates.UpdateOutcome.Available => $"Mullion {verdict.Version} is available.",
                Core.Updates.UpdateOutcome.UpToDate => "This is the latest version.",

                // Not "up to date". The check did not happen, and saying it did
                // would be a reassurance nobody earned.
                _ => "Could not check right now.",
            };
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenUpdatePage() => _host.OpenUpdatePage(UpdateUrl);

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_loading) return;

        _host.SetCheckForUpdates(value);

        // A check that already found something stays true whether or not the
        // app keeps looking, so the result is left alone here.
    }

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
        CheckForUpdates = s.CheckForUpdates;

        Surfaces = [.. s.AvailableSurfaces.Select(x => new SurfaceOption(x.Id, x.Name))];
        SelectedSurface = Surfaces.FirstOrDefault(x => x.Id == s.SurfaceId);

        Actions = [.. s.Actions.Select(a => new ActionRowViewModel(a, this))];
        OnPropertyChanged(nameof(Actions));

        OnPropertyChanged(nameof(ElevationBlurb));
        _loading = false;
    }

    /// <summary>
    /// The hotkeys that are not zones. They live here rather than on the diagram
    /// because there is nothing to point at: they act on whatever has focus, so
    /// no rectangle on the desk represents them.
    /// </summary>
    public IReadOnlyList<ActionRowViewModel> Actions { get; private set; } = [];

    /// <summary>What the action rows are saying, if anything.</summary>
    [ObservableProperty]
    private string? _actionMessage;

    /// <summary>Set while a capture is armed, so only one row asks at a time.</summary>
    [ObservableProperty]
    private string? _capturingAction;

    internal void BeginActionRebind(string command)
    {
        // Pressing the same row again calls it off, which is the only way out
        // that does not involve pressing a key you did not want to bind.
        if (CapturingAction == command)
        {
            _host.CancelRebind();
            CapturingAction = null;
            ActionMessage = null;
            return;
        }

        CapturingAction = command;
        ActionMessage = "Hold the modifier and press the key you want.";

        _host.BeginActionRebind(command, result =>
        {
            CapturingAction = null;
            ActionMessage = result.Canceled ? null : result.Message;
            if (result.Success) Reload();
        });
    }

    internal void ResetAction(string command)
    {
        _host.ResetAction(command);
        ActionMessage = null;
        Reload();
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

    /// <summary>
    /// Set by the window, which owns the file dialogs: a view model that reached
    /// for the file system directly could not be run headless.
    /// </summary>
    public Func<string, string, Task>? SaveFileRequested { get; set; }

    public Func<Task<string?>>? OpenFileRequested { get; set; }

    [RelayCommand]
    private async Task ExportLayout()
    {
        if (SaveFileRequested is null) return;

        await SaveFileRequested("mullion-layout.json", _host.ExportLayout("Mullion layout"));
        LayoutMessage = "Layout exported.";
    }

    [RelayCommand]
    private async Task ImportLayout()
    {
        if (OpenFileRequested is null) return;

        var json = await OpenFileRequested();
        if (json is null) return;

        LayoutMessage = _host.ImportLayout(json) ?? "Layout imported.";
        Reload();
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
        @"%APPDATA%\Mullion\config.json",
        @"%LOCALAPPDATA%\Mullion\logs\mullion.log",
        true,
        "Shift",
        false,
        "Win",
        [
            new ActionBindingView(GlobalAction.Undo, "Undo last move", "Win+Backspace", false),
            new ActionBindingView(GlobalAction.Minimize, "Minimize window", "Win+`", false),
        ]);

    public void BeginActionRebind(string command, Action<RebindResult> completed) =>
        completed(new RebindResult(false, "Design mode."));

    public void ResetAction(string command) { }

    public string? SetAutoStart(AutoStartMode mode) => null;
    public void BeginRebind(int row, int col, Action<RebindResult> completed) { }
    public void CancelRebind() { }
    public void ResetLayout() { }
    public bool SnapSplits => true;
    public void SetSnapSplits(bool value) { }
    public bool CanUndoZones => false;
    public bool CanRedoZones => false;
    public void UndoZones() { }
    public void RedoZones() { }

    public MonitorDiagramViewModel BuildInteractiveDiagram(
        Action<GridPos>? onZoneActivated,
        Action<string, IReadOnlyList<double>>? onSplitChanged,
        Action<string, int>? onZoneCountChanged,
        Action<string, int>? onSubzoneAxisFlipped) => new();

    public bool HasCustomZones => false;
    public bool HasCustomKeys => false;
    public void BeginZoneEdit() { }
    public void CommitZoneEdit() { }
    public void CancelZoneEdit() { }
    public void SetShowZoneFlash(bool value) { }
    public void SetAllowSpanningUnions(bool value) { }

    public void SetDragToSnap(bool enabled, string modifier) { }

    public void SetStartInTray(bool value) { }

    public void SetHotkeyModifier(string value) { }

    public IReadOnlyList<DisplayCustomization> GetCustomizations() =>
        [new DisplayCustomization("5120x1440@0,0", "Sample display", "5120 × 1440", 3, 1, 5, false, [0.25, 0.5, 0.25])];

    public void SetDisplayColumns(string slot, int columns) { }
    public void FlipSubzoneAxis(string slot, int zone) { }
    public void SetDisplayWeights(string slot, IReadOnlyList<double> weights) { }
    public void ResetAllOverrides() { }
    public string ExportLayout(string name) => "{}";
    public string? ImportLayout(string json) => null;
    public void SetWinKeySuppression(string value) { }
    public void SetKeySurface(string surfaceId) { }
    public void RestartElevated() { }
    public void OpenConfigFolder() { }
    public void OpenLogFolder() { }
    public void RerunWizard() { }
    public void SetCheckForUpdates(bool value) { }

    public Task<Core.Updates.UpdateVerdict> CheckForUpdatesNow(CancellationToken ct = default) =>
        Task.FromResult(new Core.Updates.UpdateVerdict(Core.Updates.UpdateOutcome.UpToDate, default, null));

    public void OpenUpdatePage(string? url) { }
}

/// <summary>
/// One non-zone hotkey on the settings screen.
/// <para>
/// A row rather than a control of its own, because the interesting part is the
/// capture and that already exists: it has to run through the keyboard hook,
/// since a chord with Win in it never reaches a window and no amount of
/// listening in the UI would ever see one.
/// </para>
/// </summary>
public sealed partial class ActionRowViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public ActionRowViewModel(ActionBindingView binding, SettingsViewModel owner)
    {
        _owner = owner;
        Command = binding.Command;
        Title = binding.Title;
        Chord = binding.Chord;
        IsCustom = binding.IsCustom;

        owner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.CapturingAction))
                OnPropertyChanged(nameof(IsCapturing));
        };
    }

    public string Command { get; }

    public string Title { get; }

    public string Chord { get; }

    /// <summary>Whether it has been moved off the key Mullion ships with.</summary>
    public bool IsCustom { get; }

    public bool IsCapturing => _owner.CapturingAction == Command;

    /// <summary>Waiting for a key, so the chord it shows is about to be wrong.</summary>
    public string Display => IsCapturing ? "Press a key…" : Chord;

    [RelayCommand]
    private void Rebind() => _owner.BeginActionRebind(Command);

    [RelayCommand]
    private void Reset() => _owner.ResetAction(Command);
}
