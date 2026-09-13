using Avalonia.Media;
using Mullion.App.Controls;
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


    /// <summary>
    /// The diagram and the controls that reshape it. Here rather than only in
    /// settings because this is the window the layout is already on: going to
    /// another one to change what you are looking at is a detour, not a step.
    /// </summary>
    public ZoneEditorViewModel Editor { get; }

    [ObservableProperty]
    private string _topologySummary = "Detecting displays…";

    [ObservableProperty]
    private string _hookStatus = "Not started";

    [ObservableProperty]
    private string _privilegeStatus = string.Empty;

    [ObservableProperty]
    private bool _isElevated;


    [ObservableProperty]
    private string? _simulationName;

    public bool IsSimulating => SimulationName is not null;

    public string SimulationLine =>
        $"Simulating: {SimulationName}. This is a preview of what a first launch would produce on that "
        + "arrangement — hotkeys are disabled and nothing is saved.";

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
    private bool _dragToSnap = true;

    [ObservableProperty]
    private string _dragModifierName = "Shift";

    public MainWindowViewModel() : this(new DesignAppHost()) { }

    public MainWindowViewModel(IAppHost host)
    {
        _host = host;

        // Before the first Refresh, which asks it to rebuild.
        Editor = new ZoneEditorViewModel(host);

        _host.StateChanged += Refresh;
        Refresh();
    }

    public bool HasConflicts => Conflicts.Count > 0;

    [ObservableProperty]
    private bool _showDragConflictBanner;

    [ObservableProperty]
    private string _dragConflictDetail = string.Empty;

    [ObservableProperty]
    private string _dragConflictAction = "Turn off FancyZones";

    [RelayCommand]
    private void ResolveDragConflict() => _host.ResolveDragConflict();

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

    public Action? ShowSponsorRequested { get; set; }

    /// <summary>
    /// Which build this is, for the header.
    /// <para>
    /// The version alone, not the commit. About carries the commit because a
    /// bug report needs to name an exact build; up here it would be four
    /// characters of hex nobody reading the window has any use for.
    /// </para>
    /// </summary>
    public string Version => $"v{Services.BuildInfo.Version}";

    /// <summary>
    /// Whether this build has anywhere to send anyone. False for every build
    /// made from a clean checkout, which is what keeps a fork from asking for
    /// money on someone else's behalf.
    /// </summary>
    public bool CanSponsor => Services.SponsorLink.IsOffered;

    [RelayCommand]
    private void ShowSponsor() => ShowSponsorRequested?.Invoke();

    [RelayCommand]
    private void ShowSettings() => ShowSettingsRequested?.Invoke();

    [RelayCommand]
    private void Rescan() => _host.Rescan();

    /// <summary>
    /// The one behavior the diagram cannot draw: keep the modifier held and
    /// press again, and the window grows past the zone it just went to.
    /// </summary>
    public string CycleNote =>
        $"Hold {Editor.Diagram.ModifierPrefix.TrimEnd('+')} and press a zone key again to widen " +
        "the window through its sizes. " +
        "Point at a zone to see its own sequence.";

    /// <summary>
    /// The other half of the same problem: the mouse gesture leaves no trace in
    /// the picture at all, so nothing on this screen would say it exists.
    /// <para>
    /// Named after the modifier actually configured rather than "Shift", since
    /// that is a setting and a note that lies about it is worse than none.
    /// </para>
    /// </summary>
    public string SnapNote =>
        $"Hold {DragModifierName} while dragging a window to snap it into a zone. " +
        "Drop it on the zone it already fills to return it to its old size, " +
        "and again to refill the zone.";

    /// <summary>Hidden outright when the gesture is switched off in settings.</summary>
    public bool ShowSnapNote => DragToSnap;

    /// <summary>
    /// The third thing the picture cannot show, and the least discoverable of
    /// them: the diagram draws each subzone one way round, and nothing about it
    /// suggests the other way is a keystroke away.
    /// <para>
    /// Named from the live modifier for the same reason the drag note is, since
    /// the chord is "whatever the zone is bound with, plus Shift" rather than a
    /// fixed Win+Shift.
    /// </para>
    /// </summary>
    public string FlipNote =>
        $"Press {Modifier}+Shift+[KEY] to use the dotted-line subzone split the other way instead. " +
        "The arrows while editing change which way a zone splits for good.";

    /// <summary>
    /// The configured modifier without its trailing "+", for writing chords out
    /// in prose.
    /// </summary>
    private string Modifier => Editor.Diagram.ModifierPrefix.TrimEnd('+');

    /// <summary>
    /// The hook's state, but only when it is worth reading.
    /// <para>
    /// "Active" was on screen permanently and said nothing anyone needed - the
    /// hotkeys either work, in which case you know, or they do not. What is worth
    /// saying is the opposite: a hook Windows never installed, or one the
    /// watchdog has had to put back, because that failure is otherwise completely
    /// silent. The keypress never reaches us, so there is no failed hotkey to
    /// report and nothing else on this screen would ever mention it.
    /// </para>
    /// </summary>
    /// <para>
    /// "Not started" is deliberately NOT one of them. It means the engine does
    /// not exist yet, which is true of the first snapshot - taken while the
    /// window is being built, before the host has started - and of every
    /// simulated run, where the banner already explains that hotkeys are off.
    /// Warning on it put an amber line beside the pause button at every launch
    /// about a hook that was installed and working.
    /// </para>
    public bool ShowHookWarning =>
        HookStatus.StartsWith("Not installed", StringComparison.Ordinal) ||
        HookStatus.Contains("re-armed", StringComparison.Ordinal);

    public string HookWarning => $"Hotkeys: {HookStatus}.";

    // ---- a newer release ---------------------------------------------------

    /// <summary>
    /// Version of a published release newer than this one, or null.
    /// <para>
    /// This is the only place an automatic check surfaces. Everything else
    /// Mullion has to say appears because something is wrong, so a new version
    /// gets the same treatment: one line, only when there is something to say,
    /// and gone again once the build is current.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private string? _newerVersion;

    [ObservableProperty]
    private string? _newerVersionUrl;

    public bool HasUpdate => !string.IsNullOrEmpty(NewerVersion);

    public string UpdateLine => $"Mullion {NewerVersion} available";

    [RelayCommand]
    private void OpenUpdatePage() => _host.OpenUpdatePage(NewerVersionUrl);

    /// <summary>
    /// What pressing the button will DO - a play triangle while paused, a pause
    /// bar while running. Showing the current state instead means the two look
    /// nearly identical and neither says which way round it is.
    /// </summary>
    public Geometry PauseIcon => IsPaused ? Icons.Play : Icons.Pause;

    public string PauseTooltip => IsPaused ? "Resume hotkeys" : "Pause hotkeys";

    [RelayCommand]
    private void TogglePause()
    {
        _host.Paused = !_host.Paused;
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = _host.GetSnapshot();

        Editor.Refresh();
        OnPropertyChanged(nameof(CycleNote));
        OnPropertyChanged(nameof(FlipNote));
        TopologySummary = snapshot.TopologySummary;
        HookStatus = snapshot.HookStatus;
        PrivilegeStatus = snapshot.PrivilegeStatus;
        IsPaused = snapshot.Paused;
        OnPropertyChanged(nameof(PauseIcon));
        OnPropertyChanged(nameof(PauseTooltip));
        DragToSnap = snapshot.DragToSnap;
        DragModifierName = snapshot.DragModifierName;
        OnPropertyChanged(nameof(SnapNote));
        OnPropertyChanged(nameof(FlipNote));
        OnPropertyChanged(nameof(ShowSnapNote));
        Conflicts = snapshot.Conflicts;
        ShowDragConflictBanner = snapshot.DragConflict;
        DragConflictDetail = snapshot.DragConflictDetail ?? string.Empty;
        DragConflictAction = snapshot.DragConflictAction;
        IsElevated = snapshot.IsElevated;

        BlockedByWindow = snapshot.BlockedByWindow;
        SimulationName = snapshot.SimulationName;

        NewerVersion = snapshot.NewerVersion;
        NewerVersionUrl = snapshot.NewerVersionUrl;
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateLine));

        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(IsSimulating));
        OnPropertyChanged(nameof(SimulationLine));
        OnPropertyChanged(nameof(ShowElevationBanner));
        OnPropertyChanged(nameof(ElevationHeadline));
        OnPropertyChanged(nameof(ElevationDetail));
    }
}
