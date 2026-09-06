using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;
using Mullion.Core.Abstractions;

namespace Mullion.App.ViewModels;

public sealed record BindingRow(string Key, string Zone);

public sealed record SurfaceOption(string Id, string Name)
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
    private string _winKeySuppression = "DummyKey";

    [ObservableProperty]
    private SurfaceOption? _selectedSurface;

    [ObservableProperty]
    private IReadOnlyList<SurfaceOption> _surfaces = [];

    [ObservableProperty]
    private IReadOnlyList<BindingRow> _bindings = [];

    [ObservableProperty]
    private string _configPath = string.Empty;

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

    public IReadOnlyList<string> SuppressionModes { get; } = ["DummyKey", "SwallowKeyUp", "None"];

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
        WinKeySuppression = s.WinKeySuppression;
        IsElevated = s.IsElevated;
        ConfigPath = s.ConfigPath;

        Surfaces = [.. s.AvailableSurfaces.Select(x => new SurfaceOption(x.Id, x.Name))];
        SelectedSurface = Surfaces.FirstOrDefault(x => x.Id == s.SurfaceId);
        Bindings = [.. s.Bindings.Select(b => new BindingRow(b.Key, b.Zone))];

        OnPropertyChanged(nameof(ElevationBlurb));
        _loading = false;
    }

    partial void OnStartWithWindowsChanged(bool value) => ApplyAutoStart();

    partial void OnStartElevatedChanged(bool value) => ApplyAutoStart();

    partial void OnShowZoneFlashChanged(bool value)
    {
        if (!_loading) _host.SetShowZoneFlash(value);
    }

    partial void OnAllowSpanningUnionsChanged(bool value)
    {
        if (!_loading) _host.SetAllowSpanningUnions(value);
        if (!_loading) Reload();
    }

    partial void OnWinKeySuppressionChanged(string value)
    {
        if (!_loading) _host.SetWinKeySuppression(value);
    }

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
    private void RerunWizard() => _host.RerunWizard();
}

public sealed class DesignSettingsHost : ISettingsHost
{
    public SettingsSnapshot GetSettings() => new(
        AutoStartMode.Disabled, false, false, true, true, "DummyKey",
        "left-hand",
        [("left-hand", "Left hand (QWERT / ASDFG / ZXCVB)")],
        [("Win+A", "Left"), ("Win+S", "Centre"), ("Win+D", "Right")],
        @"%APPDATA%\Mullion\config.json");

    public string? SetAutoStart(AutoStartMode mode) => null;
    public void SetShowZoneFlash(bool value) { }
    public void SetAllowSpanningUnions(bool value) { }
    public void SetWinKeySuppression(string value) { }
    public void SetKeySurface(string surfaceId) { }
    public void RestartElevated() { }
    public void OpenConfigFolder() { }
    public void RerunWizard() { }
}
