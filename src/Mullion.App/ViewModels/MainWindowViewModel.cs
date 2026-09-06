using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;

namespace Mullion.App.ViewModels;

public sealed partial class ConflictViewModel
{
    public required string Severity { get; init; }
    public required string Source { get; init; }
    public required string Summary { get; init; }
    public required string Advice { get; init; }
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppHost _host;

    [ObservableProperty]
    private MonitorDiagramViewModel _diagram = new();

    [ObservableProperty]
    private string _topologySummary = "Detecting displays…";

    [ObservableProperty]
    private string _hookStatus = "Not started";

    [ObservableProperty]
    private string _privilegeStatus = string.Empty;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private string? _blockedByWindow;

    /// <summary>
    /// Shown while Mullion is not elevated. This is not a nag: the failure it
    /// warns about is completely silent, because an elevated window's keystrokes
    /// never reach our hook at all - there is no failed hotkey to report.
    /// </summary>
    public bool ShowElevationBanner => !IsElevated;

    public string ElevationHeadline => BlockedByWindow is null
        ? "Not running as administrator"
        : $"Hotkeys are inactive right now — \"{BlockedByWindow}\"";

    public string ElevationDetail => BlockedByWindow is null
        ? "Windows hides keystrokes from Mullion while a window running as administrator has focus, "
          + "and such windows cannot be moved. Everything else works normally."
        : "That window runs as administrator, so Windows is not delivering its keystrokes to Mullion. "
          + "Hotkeys will start working again as soon as you focus another window.";

    [RelayCommand]
    private void RestartElevated() => _host.RestartElevated();

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private IReadOnlyList<ConflictViewModel> _conflicts = [];

    [ObservableProperty]
    private string _lastAction = "No hotkey pressed yet.";

    public MainWindowViewModel() : this(new DesignAppHost()) { }

    public MainWindowViewModel(IAppHost host)
    {
        _host = host;
        _host.StateChanged += Refresh;
        Refresh();
    }

    public bool HasConflicts => Conflicts.Count > 0;

    [ObservableProperty]
    private string? _trayFailure;

    public bool HasTrayFailure => TrayFailure is not null;

    /// <summary>Surface a missing tray icon rather than leaving it a mystery.</summary>
    public void ReportTrayFailure(string reason)
    {
        TrayFailure =
            $"The tray icon could not be created ({reason}). Mullion still works, but closing this " +
            "window will exit the app rather than minimising to the tray.";

        OnPropertyChanged(nameof(HasTrayFailure));
    }

    /// <summary>Set by the app shell; the view model does not create windows itself.</summary>
    public Action? ShowSettingsRequested { get; set; }

    [RelayCommand]
    private void ShowSettings() => ShowSettingsRequested?.Invoke();

    [RelayCommand]
    private void Rescan() => _host.Rescan();

    [RelayCommand]
    private void TogglePause()
    {
        _host.Paused = !_host.Paused;
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = _host.GetSnapshot();

        Diagram = snapshot.Diagram;
        TopologySummary = snapshot.TopologySummary;
        HookStatus = snapshot.HookStatus;
        PrivilegeStatus = snapshot.PrivilegeStatus;
        IsPaused = snapshot.Paused;
        LastAction = snapshot.LastAction;
        Conflicts = snapshot.Conflicts;
        IsElevated = snapshot.IsElevated;
        BlockedByWindow = snapshot.BlockedByWindow;

        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(ShowElevationBanner));
        OnPropertyChanged(nameof(ElevationHeadline));
        OnPropertyChanged(nameof(ElevationDetail));
    }
}
