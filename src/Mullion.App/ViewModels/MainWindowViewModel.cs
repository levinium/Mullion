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

        OnPropertyChanged(nameof(HasConflicts));
    }
}
