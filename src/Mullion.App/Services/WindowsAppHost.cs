#if PLATFORM_WINDOWS
using Avalonia.Threading;
using Mullion.App.ViewModels;
using Mullion.Core.Abstractions;
using Mullion.Core.Config;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Mullion.Core.Simulation;
using Mullion.Platform.Windows.Displays;
using Mullion.Platform.Windows.Hotkeys;
using Mullion.Platform.Windows.Windows;

namespace Mullion.App.Services;

/// <summary>Wires the Windows platform pieces together and exposes them to the UI.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsAppHost : IAppHost, IWizardHost, ISettingsHost, IDisposable
{
    private readonly IDisplayProvider _displayProvider;

    /// <summary>Non-null when previewing a fake arrangement rather than real hardware.</summary>
    public SimulatedTopology? Simulated { get; }

    public WindowsAppHost(SimulatedTopology? simulated = null)
    {
        Simulated = simulated;
        _displayProvider = simulated is null
            ? new WindowsDisplayProvider()
            : new SimulatedDisplayProvider(simulated);
    }
    private readonly ConfigStore _configStore = new();
    private readonly Log _log = new();

    // Constructed once config is loaded, so UndoDepth is actually honoured
    // rather than silently defaulting.
    private WindowManager? _windowsManager;

    private WindowManager Windows => _windowsManager ??= new WindowManager(_config.General.UndoDepth);

    private HotkeyEngine? _engine;
    private IReadOnlyList<DisplayInfo> _displays = [];
    private LayoutResult? _layout;
    private AppConfig _config = new();
    private string _lastAction = "No hotkey pressed yet.";
    private IReadOnlyList<ConflictViewModel> _conflicts = [];

    public event Action? StateChanged;

    public bool Paused
    {
        get => _engine?.Paused ?? false;
        set { if (_engine is not null) _engine.Paused = value; }
    }

    public bool StartInTray => _config.General.StartInTray;

    private DisplayChangeWatcher? _watcher;
    private ForegroundWatcher? _foreground;
    private string _lastFingerprint = string.Empty;

    /// <summary>Set while an elevated window has focus and hotkeys therefore cannot fire.</summary>
    private ReachState _reach = new(true, null, null);

    private void OnReachChanged(ReachState state)
    {
        _reach = state;

        _log.Info(state.Reachable
            ? "Hotkeys active again."
            : $"Hotkeys inactive: \"{state.WindowTitle}\" {state.Reason}.");

        Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
    }

    public void Start()
    {
        _config = _configStore.Load();

        _log.Info($"Mullion {BuildInfo.Full} starting. Config: {_configStore.Path_}");
        foreach (var note in _configStore.LoadNotes) _log.Warn(note);

        // Write back a migrated config once, rather than re-migrating on every
        // launch and leaving the file permanently out of date.
        if (_configStore.MigratedOnLoad) Save();

        _engine = new HotkeyEngine(Windows, ParseSuppression(_config.General.WinKeySuppression))
        {
            PauseWhenFullscreen = _config.General.PauseWhenFullscreen,
        };

        _engine.Fired += OnFired;
        _engine.Diagnostic += m => _log.Info(m);

        Rescan();

        // In simulation nothing that touches the real machine runs. The zones
        // describe monitors that are not there, so a hotkey would fling a real
        // window to coordinates off-screen; and watching for display changes
        // would immediately overwrite the simulated arrangement with the real
        // one. It is a preview, not a dry run.
        if (Simulated is not null)
        {
            _log.Info($"Simulating: {Simulated.Name}. Hotkeys and config saving are disabled.");
            StateChanged?.Invoke();
            return;
        }

        _engine.Start();

        // Monitors get plugged in, resolutions change, laptops dock. Without
        // this the app keeps snapping to zones that describe a desk that is no
        // longer there.
        _watcher = new DisplayChangeWatcher();
        _watcher.Changed += OnDisplaysChanged;

        // Only meaningful while we are NOT elevated: that is the case where
        // keystrokes are hidden from us and the failure is otherwise silent.
        if (!Elevation.IsCurrentProcessElevated)
        {
            _foreground = new ForegroundWatcher();
            _foreground.ReachChanged += OnReachChanged;
        }

        StartDragToSnap();

        // Rescan raised StateChanged before the hook existed, so the UI would
        // otherwise sit showing "Not installed" until something else changed.
        StateChanged?.Invoke();
    }

    // ---- Drag to snap -----------------------------------------------------

    private DragWatcher? _drag;
    private readonly DragZoneOverlay _dragOverlay = new();
    private IReadOnlyList<DropTarget> _dropTargets = [];
    private DropTarget? _dropHover;
    private int _dragStartWidth;
    private int _dragStartHeight;
    private bool _dragArmed;

    private FancyZonesState _fancyZones = new(false, false, false, false);

    /// <summary>
    /// Why the drag gesture is contested, in the user's terms rather than ours.
    /// </summary>
    private string? DescribeDragConflict()
    {
        if (!_fancyZones.Collides) return null;

        var gesture = _fancyZones.ShiftDrag
            ? $"{DragArmingModifier}+drag"
            : "every window drag";

        return $"PowerToys FancyZones is running and claims {gesture} as well. Both " +
               "will move the window and whichever finishes last wins, so the result " +
               "changes from drag to drag. Switch FancyZones off in PowerToys - " +
               "Mullion replaces it.";
    }

    private const string DragConflictActionText = "Open PowerToys";

    public void ResolveDragConflict()
    {
        if (!FancyZones.OpenPowerToysSettings(out var error))
        {
            _lastAction = $"Could not open PowerToys: {error}";
            _log.Warn(_lastAction);
        }
        else
        {
            _lastAction = "Opened PowerToys — switch FancyZones off there.";
        }

        RefreshDragConflict();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Re-read whether the gesture is still contested, throttled because the
    /// snapshot is rebuilt on every state change and this enumerates processes.
    /// </summary>
    private void RefreshDragConflict()
    {
        if (_drag is null) return;

        var now = DateTime.UtcNow;
        if (now - _fancyZonesCheckedAt < TimeSpan.FromSeconds(2)) return;

        _fancyZonesCheckedAt = now;
        _fancyZones = FancyZones.Detect(DragArmingModifier);
    }

    private DateTime _fancyZonesCheckedAt;
    /// <summary>Config stores the modifier by name, as it does every other enum.</summary>
    private DragModifier DragArmingModifier =>
        Enum.TryParse<DragModifier>(_config.General.DragModifier, ignoreCase: true, out var m)
            ? m
            : DragModifier.Shift;

    private void StartDragToSnap()
    {
        if (!_config.General.DragToSnap) return;

        _fancyZones = FancyZones.Detect(DragArmingModifier);

        _drag = new DragWatcher();
        _drag.DragStarted += OnDragStarted;
        _drag.DragMoved += OnDragMoved;
        _drag.DragEnded += OnDragEnded;
    }

    private void OnDragStarted(DragState state)
    {
        _dragStartWidth = state.Width;
        _dragStartHeight = state.Height;
        _dragArmed = false;
        _dropHover = null;
    }

    private void OnDragMoved(DragState state)
    {
        // Read the modifier now rather than at the drop: it is what tells a
        // deliberate snap from an ordinary window move, and the overlay has to
        // appear while the pointer is still moving to be of any use.
        if (!Modifiers.IsDown(DragArmingModifier))
        {
            if (_dragArmed) { _dragArmed = false; _dragOverlay.Hide(); }
            return;
        }

        if (!_dragArmed)
        {
            _dragArmed = true;
            _dropTargets = _layout is null ? [] : DropTargets.Build(_layout, _displays);
        }

        var hit = DropTargets.HitTest(_dropTargets, state.X, state.Y);
        if (ReferenceEquals(hit, _dropHover)) return;

        _dropHover = hit;
        _dragOverlay.Show(_dropTargets, hit);
    }

    private void OnDragEnded(DragState state)
    {
        _dragOverlay.Hide();

        var armed = _dragArmed && Modifiers.IsDown(DragArmingModifier);
        _dragArmed = false;

        if (!armed) return;

        // A resize raises the same events as a move. Dropping a resize into a
        // zone would undo the resize the user just made, which is the opposite
        // of what they asked for.
        if (state.Width != _dragStartWidth || state.Height != _dragStartHeight) return;

        var hit = DropTargets.HitTest(_dropTargets, state.X, state.Y);
        if (hit is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var result = Windows.MoveWindowTo(state.Hwnd, hit.Bounds);
            _lastAction = $"Dropped into {hit.Zone.Name}: {result.Outcome} {result.Achieved}";
            _log.Info(_lastAction);

            if (result.Outcome == MoveOutcome.Moved) _flash.Flash(result.Achieved);
            StateChanged?.Invoke();
        });
    }

    private void OnDisplaysChanged()
    {
        // The watcher coalesces the burst, but Windows also emits messages for
        // changes that leave the arrangement identical - a resolution set to
        // what it already was, a device event for something unrelated. Comparing
        // fingerprints avoids rebuilding the layout for those.
        var current = _displayProvider.GetDisplays();
        if (current.Count == 0) return;

        var signature = TopologyFingerprint.GeometrySignature(current);
        if (signature == _lastFingerprint) return;

        Dispatcher.UIThread.Post(() =>
        {
            Rescan();
            _lastAction = $"Displays changed — {_displays.Count} connected, layout reapplied.";
            StateChanged?.Invoke();
        });
    }

    public void Rescan()
    {
        _displays = _displayProvider.GetDisplays();
        if (_displays.Count == 0) return;

        _lastFingerprint = TopologyFingerprint.GeometrySignature(_displays);

        // Simulation always shows what a FIRST launch would produce. Matching a
        // stored profile would show a customised layout instead, which is the
        // opposite of what a preview of the defaults is for.
        var resolution = Simulated is null
            ? ProfileResolver.Resolve(_config, _displays)
            : new ProfileResolution(ProfileMatch.None, null, "Simulated arrangement.");

        // A stored profile is reused when the displays are recognizable, so a
        // resolution change or a rearrangement does not discard the user's
        // zones. Only genuinely new hardware generates a fresh layout.
        _layout = resolution.Match switch
        {
            ProfileMatch.Exact or ProfileMatch.SameHardware or ProfileMatch.Partial
                when resolution.Profile is not null
                => ProfileResolver.ToLayout(resolution.Profile, _displays),
            _ => LayoutBuilder.Build(_displays, KeySurface.LeftHandBlock,
                    _config.General.Shape.ToTuning(), _config.General.AllowSpanningUnions),
        };

        if (resolution.Match == ProfileMatch.None && Simulated is null)
        {
            var profile = ProfileResolver.CreateProfile(_displays, _layout);
            _config = _config with
            {
                Profiles = [.. _config.Profiles, profile],
                ActiveProfileId = profile.Id,
            };

            try { _configStore.Save(_config); }
            catch (IOException) { /* a failed save must not take the app down */ }
        }

        _engine?.Apply(_layout, _displays);
        _conflicts = DetectConflicts();

        StateChanged?.Invoke();
    }

    private IReadOnlyList<ConflictViewModel> DetectConflicts()
    {
        if (_layout is null) return [];

        return [.. HookConflictDetector
            .Detect(_layout.Zones.Select(z => _layout.Surface.ScanCodeAt(z.Position)), ChordModifiers.Win)
            .Select(c => new ConflictViewModel
            {
                Severity = c.Severity.ToString(),
                Source = c.Source,
                Summary = c.Summary,
                Advice = c.Advice,
            })];
    }

    private readonly ZoneFlashOverlay _flash = new();

    private void OnFired(HotkeyFired fired)
    {
        _lastAction = $"{fired.ZoneName} — {fired.Result.Outcome}";
        if (fired.Result.Note is not null) _lastAction += $" ({fired.Result.Note})";

        // Log successes too, not just problems. "It moved to the wrong place"
        // and "it did not fire at all" are different faults, and without a
        // record of what did fire there is no way to tell them apart after
        // the fact. The zone name and outcome only - never the key pressed.
        var key = _layout is null ? "?" : _layout.Surface.FallbackLabelAt(fired.Position);
        _log.Info($"Win+{key} -> {fired.ZoneName}: {fired.Result.Outcome} {fired.Result.Achieved}" +
                  (fired.Result.Attempts > 1 ? $" after {fired.Result.Attempts} attempts" : string.Empty));

        // Only flash on a successful placement: flashing a zone the window did
        // not reach would assert something untrue.
        if (_config.General.ShowZoneFlash && fired.Result.Success)
            _flash.Flash(fired.Result.Achieved);

        // The hotkey executor runs on its own thread; UI state must be touched
        // on the UI thread.
        Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
    }

    public AppSnapshot GetSnapshot()
    {
        var health = _engine?.Health;

        var hookStatus = health is null
            ? "Not started"
            : health.Installed
                ? $"Active · p50 {health.LatencyP50Ms:0.###} ms · max {health.LatencyMaxMs:0.##} ms · " +
                  $"{health.ReinstallCount} reinstall(s) · budget {health.LowLevelHooksTimeoutMs} ms"
                : "Not installed";

        // When hotkeys are actually blocked right now, say so concretely and
        // name the window - that is far more useful than the general caveat,
        // and it is the only warning the user will ever get, because the
        // keypress itself never reaches us.
        var privilege = !_reach.Reachable
            ? $"Hotkeys are inactive right now — \"{_reach.WindowTitle}\" {_reach.Reason}. "
              + "Restart Mullion as administrator to manage windows like this one."
            : Elevation.IsCurrentProcessElevated
                ? "Running elevated — windows that run as administrator can be managed."
                : "Not elevated — hotkeys will not fire while a window running as administrator has focus.";

        var summary = _displays.Count == 0
            ? "No displays detected"
            : $"{_displays.Count} display{(_displays.Count == 1 ? "" : "s")} · " +
              string.Join(" · ", _displays.Select(d => $"{d.Bounds.Width}×{d.Bounds.Height}"));

        RefreshDragConflict();

        return new AppSnapshot(
            MonitorDiagramViewModel.Build(_displays, _layout),
            summary,
            hookStatus,
            privilege,
            Paused,
            _lastAction,
            _conflicts,
            Elevation.IsCurrentProcessElevated,
            _reach.Reachable ? null : _reach.WindowTitle,
            Simulated?.Name,
            _fancyZones.Collides,
            DescribeDragConflict(),
            DragConflictActionText);
    }

    // ---- wizard ------------------------------------------------------------

    private IReadOnlyList<LayoutCandidate> _candidates = [];

    /// <summary>Set when the wizard is previewing, so it can be discarded on cancel.</summary>
    private LayoutResult? _committedLayout;

    public bool NeedsWizard => !_config.WizardCompleted;

    public Action? WizardCloseRequested { get; set; }

    public WizardSnapshot GetWizardSnapshot()
    {
        _candidates = LayoutCandidates.Generate(
            _displays, KeySurface.LeftHandBlock,
            _config.General.Shape.ToTuning(),
            _config.General.AllowSpanningUnions);

        var choices = _candidates.Select(c => new LayoutChoiceViewModel
        {
            Id = c.Id,
            Name = c.Name,
            Rationale = c.Rationale,
            ZoneCount = c.Layout.Zones.Count,
            KeySummary = SummariseKeys(c.Layout),
            Preview = MonitorDiagramViewModel.Build(_displays, c.Layout),
        }).ToList();

        return new WizardSnapshot(
            MonitorDiagramViewModel.Build(_displays, null),
            _displays.Count == 0
                ? "No displays detected"
                : $"{_displays.Count} display{(_displays.Count == 1 ? "" : "s")} · " +
                  string.Join(" · ", _displays.Select(d => $"{d.FriendlyName} {d.Bounds.Width}×{d.Bounds.Height}")),
            choices,
            _conflicts);
    }

    /// <summary>
    /// Applies the candidate immediately so the choice can be felt, not just
    /// seen. The previous layout is remembered so cancelling restores it.
    /// </summary>
    public void PreviewLayout(string candidateId)
    {
        var candidate = _candidates.FirstOrDefault(c => c.Id == candidateId);
        if (candidate is null) return;

        _committedLayout ??= _layout;
        _layout = candidate.Layout;
        _engine?.Apply(_layout, _displays);

        StateChanged?.Invoke();
    }

    public void CommitLayout(string candidateId)
    {
        var candidate = _candidates.FirstOrDefault(c => c.Id == candidateId);
        if (candidate is null) return;

        _layout = candidate.Layout;
        _committedLayout = null;
        _engine?.Apply(_layout, _displays);

        var profile = ProfileResolver.CreateProfile(_displays, _layout);

        // Replace any profile for this exact arrangement rather than piling up
        // near-duplicates every time the wizard is re-run.
        _config = _config with
        {
            WizardCompleted = true,
            Profiles = [.. _config.Profiles.Where(p =>
                p.ArrangementFingerprint != profile.ArrangementFingerprint), profile],
            ActiveProfileId = profile.Id,
        };

        try { _configStore.Save(_config); }
        catch (IOException) { /* a failed save must not take the app down */ }

        StateChanged?.Invoke();
    }

    public void CloseWizard()
    {
        // Restore whatever was live before previewing, if nothing was committed.
        if (_committedLayout is not null)
        {
            _layout = _committedLayout;
            _committedLayout = null;
            if (_layout is not null) _engine?.Apply(_layout, _displays);
        }

        WizardCloseRequested?.Invoke();
    }

    // ---- settings ----------------------------------------------------------

    private readonly WindowsAutoStartService _autoStart = new();

    public Action? RerunWizardRequested { get; set; }

    public SettingsSnapshot GetSettings()
    {
        var status = _autoStart.GetStatus();

        var bindings = _layout is null
            ? []
            : _layout.Zones
                .OrderBy(z => z.Position.Row).ThenBy(z => z.Position.Col)
                .Select(z => new BindingEntry(
                    $"Win+{_layout.Surface.FallbackLabelAt(z.Position)}",
                    z.Name,
                    z.Position.Row,
                    z.Position.Col))
                .ToList();

        return new SettingsSnapshot(
            status.Mode,
            Elevation.IsCurrentProcessElevated,
            Elevation.IsCurrentProcessElevated,
            _config.General.ShowZoneFlash,
            _config.General.AllowSpanningUnions,
            _config.General.WinKeySuppression,
            _layout?.Surface.Id ?? KeySurface.LeftHandBlock.Id,
            [.. KeySurface.All.Select(s => (s.Id, s.Name))],
            bindings,
            _configStore.Path_,
            _log.Path_,
            _config.General.DragToSnap,
            _config.General.DragModifier,
            _config.General.StartInTray);
    }

    public void OpenLogFolder() => OpenFolder(Path.GetDirectoryName(_log.Path_));

    public string? SetAutoStart(AutoStartMode mode)
    {
        if (!_autoStart.TrySetMode(mode, out var error)) return error;

        UpdateGeneral(g => g with { AutoStart = mode.ToString() });
        return null;
    }

    public void SetShowZoneFlash(bool value) => UpdateGeneral(g => g with { ShowZoneFlash = value });

    public void SetAllowSpanningUnions(bool value)
    {
        UpdateGeneral(g => g with { AllowSpanningUnions = value });

        // This changes which zones exist, so the layout has to be rebuilt.
        Regenerate();
    }

    public void SetStartInTray(bool value) => UpdateGeneral(g => g with { StartInTray = value });

    public void SetDragToSnap(bool enabled, string modifier)
    {
        UpdateGeneral(g => g with { DragToSnap = enabled, DragModifier = modifier });

        // Applied live rather than at the next launch: the watcher is cheap to
        // install and tear down, and a setting that needs a restart to try is
        // one nobody tries.
        if (_drag is not null)
        {
            _drag.Dispose();
            _drag = null;
            _dragOverlay.Hide();
        }

        if (enabled && Simulated is null) StartDragToSnap();

        _fancyZonesCheckedAt = default;
        RefreshDragConflict();
        StateChanged?.Invoke();
    }

    public void SetWinKeySuppression(string value)
    {
        UpdateGeneral(g => g with { WinKeySuppression = value });

        // Applied live: the whole point of offering alternatives is that the
        // user can try one when the Start menu misbehaves, without a restart.
        if (_engine is not null) _engine.Suppression = ParseSuppression(value);
    }

    public void SetKeySurface(string surfaceId)
    {
        var surface = KeySurface.All.FirstOrDefault(s => s.Id == surfaceId);
        if (surface is null || _displays.Count == 0) return;

        _layout = LayoutBuilder.Build(
            _displays, surface, _config.General.Shape.ToTuning(), _config.General.AllowSpanningUnions);

        _engine?.Apply(_layout, _displays);
        PersistCurrentLayout();
        StateChanged?.Invoke();
    }

    public void RestartElevated()
    {
        if (Elevation.TryRestartElevated("--tray")) Environment.Exit(0);
    }

    public void OpenConfigFolder() => OpenFolder(Path.GetDirectoryName(_configStore.Path_));

    private static void OpenFolder(string? folder)
    {
        if (folder is null) return;

        Directory.CreateDirectory(folder);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    public void RerunWizard() => RerunWizardRequested?.Invoke();

    public void BeginRebind(int row, int col, Action<RebindResult> completed)
    {
        if (_engine is null || _layout is null)
        {
            completed(new RebindResult(false, "Hotkeys are not running."));
            return;
        }

        var surface = _layout.Surface;

        _engine.BeginCapture((mods, scan) => Dispatcher.UIThread.Post(() =>
            completed(ApplyRebind(row, col, mods, scan, surface))));
    }

    public void CancelRebind() => _engine?.EndCapture();

    private RebindResult ApplyRebind(
        int row, int col, ChordModifiers mods, ushort scan, KeySurface surface)
    {
        if (_layout is null) return new RebindResult(false, "No layout.");

        if (!mods.HasFlag(ChordModifiers.Win))
            return new RebindResult(false, "Hold Win while pressing the key you want.");

        var target = LayoutEditor.PositionOfScanCode(surface, scan);
        if (target is null)
        {
            return new RebindResult(false,
                $"That key is not part of the {surface.Name} block. Choose a different key surface " +
                "if you want keys outside it.");
        }

        var outcome = LayoutEditor.Rebind(_layout, new GridPos(row, col), target.Value);
        if (!outcome.Success) return new RebindResult(false, outcome.Message);

        _layout = outcome.Layout;
        _engine?.Apply(_layout, _displays);
        PersistCurrentLayout();
        StateChanged?.Invoke();

        return new RebindResult(true, outcome.Message);
    }

    public MonitorDiagramViewModel BuildInteractiveDiagram(Action<GridPos> onZoneActivated) =>
        MonitorDiagramViewModel.Build(_displays, _layout, onZoneActivated);

    public void ResetLayout()
    {
        if (_displays.Count == 0) return;

        var surface = KeySurface.All.FirstOrDefault(s => s.Id == _layout?.Surface.Id) ?? KeySurface.LeftHandBlock;

        _layout = LayoutBuilder.Build(
            _displays, surface, _config.General.Shape.ToTuning(), _config.General.AllowSpanningUnions);

        _engine?.Apply(_layout, _displays);
        PersistCurrentLayout();
        StateChanged?.Invoke();
    }

    private void Regenerate()
    {
        if (_displays.Count == 0) return;

        var surface = KeySurface.All.FirstOrDefault(s => s.Id == _layout?.Surface.Id) ?? KeySurface.LeftHandBlock;

        _layout = LayoutBuilder.Build(
            _displays, surface, _config.General.Shape.ToTuning(), _config.General.AllowSpanningUnions);

        _engine?.Apply(_layout, _displays);
        PersistCurrentLayout();
        StateChanged?.Invoke();
    }

    private void PersistCurrentLayout()
    {
        if (_layout is null || _displays.Count == 0) return;

        var profile = ProfileResolver.CreateProfile(_displays, _layout);

        _config = _config with
        {
            Profiles = [.. _config.Profiles.Where(p =>
                p.ArrangementFingerprint != profile.ArrangementFingerprint), profile],
            ActiveProfileId = profile.Id,
        };

        Save();
    }

    private void UpdateGeneral(Func<GeneralSettings, GeneralSettings> update)
    {
        _config = _config with { General = update(_config.General) };
        Save();
        StateChanged?.Invoke();
    }

    private void Save()
    {
        // A simulated arrangement must never reach the real config: its
        // profiles describe monitors that do not exist.
        if (Simulated is not null) return;

        try { _configStore.Save(_config); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A failed save must not take the app down; the in-memory state is
            // still correct for this session.
            Diagnostic($"Could not save settings: {e.Message}");
        }
    }

    private void Diagnostic(string message) => _lastAction = message;

    private static string SummariseKeys(LayoutResult layout) =>
        string.Join(" / ", Enumerable.Range(0, layout.Surface.Rows)
            .Select(row => string.Join(" ", Enumerable.Range(0, layout.Surface.Cols)
                .Where(col => layout.At(row, col) is not null)
                .Select(col => layout.Surface.FallbackLabelAt(new GridPos(row, col)))))
            .Where(s => s.Length > 0));

    private static WinKeySuppression ParseSuppression(string value) =>
        Enum.TryParse<WinKeySuppression>(value, ignoreCase: true, out var parsed)
            ? parsed
            : WinKeySuppression.DummyKey;

    public void Dispose()
    {
        _foreground?.Dispose();
        _watcher?.Dispose();
        _drag?.Dispose();
        _dragOverlay.Dispose();
        _engine?.Dispose();
        _flash.Dispose();
        _log.Info("Mullion stopping.");
        _log.Dispose();
    }
}
#endif

