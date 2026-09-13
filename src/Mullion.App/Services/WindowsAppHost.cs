using Mullion.Core.Geometry;
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

    /// <summary>
    /// A pretend desk keeps its config to itself.
    /// <para>
    /// Sharing the real one was destructive rather than untidy. A simulated
    /// topology cannot match the fingerprint of the actual monitors, so the host
    /// generated a fresh profile and saved it over the one belonging to the real
    /// desk - taking the key surface and every zone edit with it. The test suite
    /// builds these by the dozen, so a test run quietly reset the configuration
    /// of whoever ran it, and so did every --simulate screenshot pass.
    /// </para>
    /// </summary>
    public WindowsAppHost(SimulatedTopology? simulated = null, string? configPath = null)
    {
        Simulated = simulated;
        _displayProvider = simulated is null
            ? new WindowsDisplayProvider()
            : new SimulatedDisplayProvider(simulated);

        _configStore = new ConfigStore(
            configPath ?? (simulated is null ? null : ConfigStore.ForSimulation(simulated.Id)));
    }

    private readonly ConfigStore _configStore;
    private readonly Log _log = new();

    // Constructed once config is loaded, so UndoDepth is actually honored
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

    /// <summary>
    /// The actions that are not zones, defaults with any stored changes over them.
    /// </summary>
    private IReadOnlyList<GlobalAction> Actions =>
        GlobalAction.Resolve(_config.Actions.Select(a =>
            (a.Command,
             KeyText.Read(a.Key),
             string.IsNullOrWhiteSpace(a.Modifier)
                 ? (ChordModifiers?)null
                 : ModifierChoice.Parse(a.Modifier))));

    /// <summary>The modifier every zone hotkey is taken with.</summary>
    private ChordModifiers HotkeyModifier => ModifierChoice.Parse(_config.General.HotkeyModifier);

    private string ModifierPrefix => $"{ModifierChoice.Format(HotkeyModifier)}+";

    public void SetHotkeyModifier(string value)
    {
        UpdateGeneral(g => g with { HotkeyModifier = value });

        // Applied live, and the conflict list is rebuilt with it: which other
        // programs collide depends entirely on which modifier is in play.
        if (_layout is not null) _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);

        _conflicts = DetectConflicts();
        StateChanged?.Invoke();
    }

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

        // Seeded so the first edit has somewhere to go back to.
        _history = new LayoutHistory(Now);

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

        // Seeded here rather than at the top of Start, because this is the
        // first moment there is a layout to seed it with. Seeded before that,
        // the state undo returns to had no keys on it at all - and a rebind,
        // which changes nothing but keys, compared equal to it and was never
        // recorded.
        _history.Reset(Now);

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

    /// <summary>
    /// Where the window was when it was picked up.
    /// <para>
    /// The question the toggle turns on is whether the window was ALREADY
    /// filling the zone it is being dropped into, and by the time it is dropped
    /// it is wherever the pointer left it - several hundred pixels from the
    /// zone it started in, and no longer matching it at all. Asked of the drop
    /// position, the answer was always no and the gesture always just refilled
    /// the zone, which is exactly what it looked like from the outside.
    /// </para>
    /// </summary>
    private PxRect? _dragStartBounds;
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
        _dragStartBounds = Windows.BoundsOf(state.Hwnd);
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

        // Shown as the pointer moves, not worked out again at the drop: the
        // overlay is a promise about what letting go will do, and one made from
        // different inputs than the drop uses is a promise that can be broken.
        var plan = PlanDropInto(state.Hwnd, hit);

        _dragOverlay.Show(_dropTargets, hit, plan?.Target);
    }

    /// <summary>
    /// What letting go over this zone would do, or null when there is no zone
    /// under the pointer.
    /// </summary>
    private DropPlan? PlanDropInto(nint hwnd, DropTarget? hit) =>
        hit is null
            ? null
            : ZoneFit.Plan(_dragStartBounds, Windows.ChosenSizeOf(hwnd), hit.Bounds);

    /// <summary>
    /// Put the drag overlay on screen in the state that only exists halfway
    /// through a gesture, so it can be looked at and photographed.
    /// <para>
    /// Reachable no other way: what it draws depends on a window having been
    /// picked up, a zone being under the pointer, and a remembered size to go
    /// back to, and all three are gone the instant the mouse button comes up.
    /// </para>
    /// </summary>
    public void PreviewDragOverlay()
    {
        if (_layout is null) return;

        var targets = DropTargets.Build(_layout, _displays);
        if (targets.Count == 0) return;

        var hit = targets[targets.Count / 2];
        var window = new PxRect(0, 0, hit.Bounds.Width / 2, hit.Bounds.Height / 2);

        _dragOverlay.Show(targets, hit, ZoneFit.Restore(window, hit.Bounds));
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
            // Dropped on the zone it was already filling. That used to accept
            // the gesture and do nothing; it is the way back now - out to fill
            // the zone, then back to the size it had before, then out again.
            //
            // The size to go back to is the window manager's to answer, because
            // it sees every move Mullion makes. Remembered here instead, it
            // would only have covered windows dropped into zones by hand, and a
            // window filled with a hotkey had nothing to return to.
            var plan = PlanDropInto(state.Hwnd, hit)!.Value;

            var result = Windows.MoveWindowTo(state.Hwnd, plan.Target);

            _lastAction = plan.Restoring
                ? $"Restored in {hit.Zone.Name}: {result.Outcome} {result.Achieved}"
                : $"Dropped into {hit.Zone.Name}: {result.Outcome} {result.Achieved}";

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

        // The states that were undoable describe monitors that may no longer
        // be attached, and undoing onto them would apply a layout for a desk
        // that is not there. An open edit session goes the same way, and is
        // kept rather than dropped: its baseline describes the old desk too.
        CommitZoneEdit();
        _history.Reset(Now);
        if (_displays.Count == 0) return;

        _lastFingerprint = TopologyFingerprint.GeometrySignature(_displays);

        // Simulation always shows what a FIRST launch would produce. Matching a
        // stored profile would show a customized layout instead, which is the
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
                    _config.General.Shape.ToTuning(), _config.General.AllowSpanningUnions,
                    _config.Overrides),
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

        _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
        _conflicts = DetectConflicts();

        // Rescan is the button people press after changing something outside
        // Mullion, and that is at least as often another app as it is a monitor.
        // Clearing the throttle makes the next snapshot re-read PowerToys rather
        // than answer from a reading taken up to two seconds ago - so switching
        // FancyZones off and pressing rescan clears the banner, which is what
        // pressing it plainly promises.
        _fancyZonesCheckedAt = DateTime.MinValue;

        StateChanged?.Invoke();
    }

    private IReadOnlyList<ConflictViewModel> DetectConflicts()
    {
        if (_layout is null) return [];

        return [.. HookConflictDetector
            .Detect(_layout.Zones.Select(z => _layout.Surface.ScanCodeAt(z.Position)), HotkeyModifier)
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
        // Named by the key it actually answers to, not by where it sits on the
        // grid. A zone moved onto F5 still lives at the "A" cell, so the surface
        // label reported "Win+A" for a press of Win+F5.
        var zone = _layout?.Zones.FirstOrDefault(z => z.Position == fired.Position);
        var key = _layout is null || zone is null
            ? "?"
            : KeyNames.Of(zone.KeyOn(_layout.Surface));
        _log.Info($"{ModifierChoice.Format(HotkeyModifier)}+{key} -> {fired.ZoneName}: {fired.Result.Outcome} {fired.Result.Achieved}" +
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

        // Just the state when all is well. The latency and re-arm counts are
        // what you want the moment something is wrong and noise every other
        // moment - and this line is on the window people leave open, so it has
        // to earn its space. Re-arms are the exception: they mean Windows
        // dropped the hook and the watchdog put it back, which is worth saying
        // out loud because it is otherwise completely silent.
        var hookStatus = health is null
            ? "Not started"
            : !health.Installed
                ? "Not installed"
                : health.ReinstallCount > 0
                    ? $"Active, re-armed {health.ReinstallCount} time" +
                      (health.ReinstallCount == 1 ? "" : "s")
                    : "Active";

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
            MonitorDiagramViewModel.Build(_displays, _layout, defaultModifier: HotkeyModifier),
            summary,
            hookStatus,
            privilege,
            Paused,
            _conflicts,
            Elevation.IsCurrentProcessElevated,
            _reach.Reachable ? null : _reach.WindowTitle,
            Simulated?.Name,
            _fancyZones.Collides,
            DescribeDragConflict(),
            DragConflictActionText,
            _config.General.DragToSnap,
            DragArmingModifier.ToString());
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
            Preview = MonitorDiagramViewModel.Build(_displays, c.Layout, defaultModifier: HotkeyModifier),
        }).ToList();

        return new WizardSnapshot(
            MonitorDiagramViewModel.Build(_displays, null, defaultModifier: HotkeyModifier),
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
        _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);

        StateChanged?.Invoke();
    }

    public void CommitLayout(string candidateId)
    {
        var candidate = _candidates.FirstOrDefault(c => c.Id == candidateId);
        if (candidate is null) return;

        _layout = candidate.Layout;
        _committedLayout = null;
        _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);

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

        // Save(), not the store directly. Writing straight through skipped the
        // two refusals every other write in this class honors: a simulated desk
        // must never reach the real config, since its profiles describe monitors
        // that do not exist, and an open edit session holds writes back so
        // cancelling restores nothing from disk. Finishing the wizard was the
        // one path that bypassed both.
        Save();

        StateChanged?.Invoke();
    }

    public void CloseWizard()
    {
        // Restore whatever was live before previewing, if nothing was committed.
        if (_committedLayout is not null)
        {
            _layout = _committedLayout;
            _committedLayout = null;
            if (_layout is not null) _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
        }

        WizardCloseRequested?.Invoke();
    }

    // ---- settings ----------------------------------------------------------

    private readonly WindowsAutoStartService _autoStart = new();

    public Action? RerunWizardRequested { get; set; }

    public SettingsSnapshot GetSettings()
    {
        var status = _autoStart.GetStatus();

        return new SettingsSnapshot(
            status.Mode,
            Elevation.IsCurrentProcessElevated,
            Elevation.IsCurrentProcessElevated,
            _config.General.ShowZoneFlash,
            _config.General.AllowSpanningUnions,
            _config.General.WinKeySuppression,
            _layout?.Surface.Id ?? KeySurface.LeftHandBlock.Id,
            [.. KeySurface.All.Select(s => (s.Id, s.Name))],
            _configStore.Path_,
            _log.Path_,
            _config.General.DragToSnap,
            _config.General.DragModifier,
            _config.General.StartInTray,
            ModifierChoice.Format(HotkeyModifier),
            DescribeActions());
    }

    /// <summary>
    /// The non-zone hotkeys as the settings screen shows them: what each is
    /// called, the chord it answers to, and whether it has been moved off the
    /// key Mullion ships with.
    /// </summary>
    private IReadOnlyList<ActionBindingView> DescribeActions()
    {
        var defaults = GlobalAction.Defaults.ToDictionary(a => a.Command, StringComparer.Ordinal);

        return
        [
            .. Actions.Select(a => new ActionBindingView(
                a.Command,
                a.Title,
                $"{ModifierChoice.Format(a.ChordWith(HotkeyModifier))}+{KeyNames.Of(a.Key)}",
                !defaults.TryGetValue(a.Command, out var shipped)
                    || shipped.Key != a.Key
                    || a.Modifier is not null))
        ];
    }

    public void BeginActionRebind(string command, Action<RebindResult> completed)
    {
        if (_engine is null)
        {
            completed(new RebindResult(false, "Hotkeys are not running."));
            return;
        }

        _engine.BeginCapture(
            (mods, key) => Dispatcher.UIThread.Post(() => completed(ApplyActionRebind(command, mods, key))),
            () => Dispatcher.UIThread.Post(
                () => completed(new RebindResult(false, string.Empty, Canceled: true))));
    }

    private RebindResult ApplyActionRebind(string command, ChordModifiers mods, KeyStroke key)
    {
        // Same rule as a zone: any modifier will do, but a bare key would fire
        // while typing.
        if (mods == ChordModifiers.None)
        {
            return new RebindResult(false,
                "Hold at least one modifier - Win, Ctrl, Alt or Shift - while pressing the key.");
        }

        if (!KeyNames.IsBindable(key))
            return new RebindResult(false, "That key cannot be used for a hotkey.");

        var title = GlobalAction.Describe(command);

        // An action landing on a zone's chord would be a hotkey that does two
        // things, and the zone would quietly win - the engine binds those first.
        if (_layout is not null)
        {
            var clash = _layout.Zones.FirstOrDefault(z =>
                z.KeyOn(_layout.Surface) == key && z.ChordWith(HotkeyModifier) == mods);

            if (clash is not null)
            {
                return new RebindResult(false,
                    $"{ModifierChoice.Format(mods)}+{KeyNames.Of(key)} already goes to \"{clash.Name}\".");
            }
        }

        var chosen = mods == HotkeyModifier ? null : ModifierChoice.Format(mods);

        _config = _config with
        {
            Actions =
            [
                .. _config.Actions.Where(a => a.Command != command),
                new ActionRecord(command, KeyText.Write(key)!, chosen),
            ],
        };

        Save();
        if (_layout is not null) _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
        StateChanged?.Invoke();

        return new RebindResult(true, $"{title} is now {ModifierChoice.Format(mods)}+{KeyNames.Of(key)}.");
    }

    public void ResetAction(string command)
    {
        _config = _config with { Actions = [.. _config.Actions.Where(a => a.Command != command)] };

        Save();
        if (_layout is not null) _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
        StateChanged?.Invoke();
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

    // ---- Hand-made splits --------------------------------------------------

    public IReadOnlyList<DisplayCustomization> GetCustomizations()
    {
        if (_layout is null) return [];

        var hasOthers = _displays.Count > 1;
        var tuning = _config.General.Shape.ToTuning();
        var bySlot = _config.Overrides.ToDictionary(o => o.Slot, StringComparer.Ordinal);

        return
        [
            .. _displays.Select(d =>
            {
                var slot = DisplaySlot.Of(d);
                var counts = ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, hasOthers, tuning);
                var zones = ZonesAcross(d);

                return new DisplayCustomization(
                    slot,
                    d.FriendlyName,
                    $"{d.Bounds.Width} × {d.Bounds.Height}",
                    zones.Count,

                    // The analyzer's own bounds, so stepping cannot produce a
                    // split it would itself have rejected as unusable. One is
                    // always allowed: "leave this display whole" is a legitimate
                    // answer even where the analyzer would rather split it.
                    Math.Min(1, counts.Min),
                    Math.Max(counts.Max, zones.Count),
                    bySlot.ContainsKey(slot),
                    zones.Weights);
            }),
        ];
    }

    /// <summary>
    /// The zones actually drawn across this display, and their relative sizes.
    /// Read from the layout rather than recomputed, so what the UI shows is what
    /// is on screen even when the two could differ.
    /// </summary>
    private (int Count, IReadOnlyList<double> Weights) ZonesAcross(DisplayInfo display)
    {
        if (_layout is null) return (1, [1]);

        var home = _layout.Surface.HomeRow;

        var areas = _layout.Zones
            .Where(z => z.Position.Row == home && z.Kind != ZoneKind.Union)
            .SelectMany(z => z.Parts.Where(p => p.DisplayKey == display.StableKey).Select(p => p.Area))
            .OrderBy(a => a.X)
            .ToList();

        if (areas.Count == 0) return (1, [1]);

        return (areas.Count, [.. areas.Select(a => a.W)]);
    }

    /// <summary>
    /// Undo and redo for zone shape edits - seam drags and zone counts.
    /// <para>
    /// Deliberately not key rebinds. Those live in the layout rather than the
    /// overrides, and a stack mixing the two would have to undo onto zones that
    /// regenerating the layout has already replaced. "Reset keys to default" is
    /// the way back from a rebind.
    /// </para>
    /// </summary>
    private LayoutHistory _history = new();

    /// <summary>Snap dragged splits to the grid and to exact-aspect positions.</summary>
    public bool SnapSplits => _config.General.SnapSplits;

    public void SetSnapSplits(bool value)
    {
        _config = _config with { General = _config.General with { SnapSplits = value } };
        Save();
        StateChanged?.Invoke();
    }

    /// <summary>The state as it stands: zone shapes, and the keys on them.</summary>
    private LayoutSnapshot Now => new(_config.Overrides, _layout);

    public bool CanUndoZones => _history.CanUndo;

    public bool CanRedoZones => _history.CanRedo;

    public void UndoZones() => Restore(_history.Undo());

    public void RedoZones() => Restore(_history.Redo());

    /// <summary>
    /// Put a remembered state back: the shapes from its override set, and the
    /// keys from the layout it was taken with.
    /// <para>
    /// The keys have to come from the snapshot rather than from the layout as
    /// it now stands, or undoing a rebind would rebuild the shapes and then
    /// carry the very rebind being undone straight back onto them.
    /// </para>
    /// </summary>
    private void Restore(LayoutSnapshot? snapshot)
    {
        if (snapshot is null) return;

        _config = _config with { Overrides = [.. snapshot.Overrides] };
        Save();
        RegenerateFrom(snapshot.Layout);
    }

    public void SetDisplayColumns(string slot, int columns)
    {
        // Changing the count invalidates weights meant for the old one, so they
        // are dropped rather than left to be silently ignored later.
        UpdateOverride(slot, o => new DisplayOverride(slot, columns, null));
    }

    // ---- Edit sessions ------------------------------------------------------

    /// <summary>
    /// An open edit session: what to go back to, and whether anything is waiting
    /// to be written.
    /// <para>
    /// Edits apply immediately - the diagram has to show what a drag did, and
    /// the hotkeys may as well work on it while it is open - but they are not
    /// WRITTEN until the session is confirmed. That is what makes cancelling
    /// mean something rather than being a second undo stack.
    /// </para>
    /// </summary>
    private AppConfig? _editBaseline;

    private LayoutResult? _editBaselineLayout;
    private bool _editing;
    private bool _writePending;

    public void BeginZoneEdit()
    {
        _editBaseline = _config;
        _editBaselineLayout = _layout;
        _editing = true;
        _writePending = false;
    }

    public void CommitZoneEdit()
    {
        _editing = false;
        _editBaseline = null;
        _editBaselineLayout = null;

        if (!_writePending) return;

        _writePending = false;
        Save();
    }

    public void CancelZoneEdit()
    {
        if (_editBaseline is null)
        {
            _editing = false;
            return;
        }

        _config = _editBaseline;
        _layout = _editBaselineLayout;

        // The undone states describe a layout that is being thrown away, so
        // undo after a cancel would walk back into edits the user just
        // discarded.
        _history.Reset(Now);

        _editing = false;
        _writePending = false;
        _editBaseline = null;
        _editBaselineLayout = null;

        // Nothing to write: not writing during the session is exactly what
        // leaves the file already holding this.
        if (_layout is not null) _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Whether any key has been moved off where the allocator would have put it.
    /// <para>
    /// Answered by generating the layout again and comparing, rather than by
    /// keeping a flag: a flag has to be set everywhere a key can move and
    /// cleared everywhere one can move back, and the first path that forgets
    /// leaves the button lying about whether there is anything to undo.
    /// </para>
    /// <para>
    /// Zones are matched by the geometry they cover, since regenerating gives
    /// every zone a fresh identity. The shapes are the same either way - only
    /// which key sits on which zone can differ.
    /// </para>
    /// </summary>
    public bool HasCustomKeys
    {
        get
        {
            if (_layout is null || _displays.Count == 0) return false;

            var surface = KeySurface.All.FirstOrDefault(s => s.Id == _layout.Surface.Id)
                          ?? KeySurface.LeftHandBlock;

            var fresh = LayoutBuilder.Build(
                _displays, surface, _config.General.Shape.ToTuning(),
                _config.General.AllowSpanningUnions, _config.Overrides);

            return !LayoutEditor.SameKeyAssignments(_layout, fresh);
        }
    }

    /// <summary>
    /// Whether the zones differ from the ones the engine would derive on its
    /// own - asked by generating those and comparing, exactly as
    /// <see cref="HasCustomKeys"/> does.
    /// <para>
    /// It used to count override records instead, and an override is not the
    /// same thing as a difference: a stored "three columns" on a display the
    /// engine already splits into three is a record of a choice that happens to
    /// agree with the default. The reset button was lit for it and did nothing
    /// when pressed.
    /// </para>
    /// </summary>
    public bool HasCustomZones
    {
        get
        {
            if (_layout is null || _displays.Count == 0) return false;

            var surface = KeySurface.All.FirstOrDefault(s => s.Id == _layout.Surface.Id)
                          ?? KeySurface.LeftHandBlock;

            var derived = LayoutBuilder.Build(
                _displays, surface, _config.General.Shape.ToTuning(),
                _config.General.AllowSpanningUnions);

            return !LayoutEditor.SameZoneShapes(_layout, derived);
        }
    }

    public void SetDisplayWeights(string slot, IReadOnlyList<double> weights)
    {
        UpdateOverride(slot, o => new DisplayOverride(slot, weights.Count, [.. weights]));
    }

    public void ResetAllOverrides()
    {
        _config = _config with { Overrides = [] };
        _history.Record(Now);
        Save();
        Regenerate();
    }

    private void UpdateOverride(string slot, Func<DisplayOverride?, DisplayOverride> change)
    {
        var existing = _config.Overrides.FirstOrDefault(o => o.Slot == slot);
        var replaced = _config.Overrides.Where(o => o.Slot != slot).ToList();

        replaced.Add(change(existing));

        _config = _config with { Overrides = replaced };
        _history.Record(Now);
        Save();
        Regenerate();
    }

    public string ExportLayout(string name) => LayoutPackageIo.Serialize(
        LayoutPackageIo.Export(
            _config.Overrides,
            [.. _displays.Select(DisplaySlot.Of)],
            _layout?.Surface.Id ?? KeySurface.LeftHandBlock.Id,
            name));

    public string? ImportLayout(string json)
    {
        var package = LayoutPackageIo.Deserialize(json, out var error);
        if (package is null) return error;

        var preview = LayoutPackageIo.Preview(package, [.. _displays.Select(DisplaySlot.Of)]);

        _config = _config with
        {
            Overrides = LayoutPackageIo.Merge(_config.Overrides, package.Overrides),
        };

        Save();

        if (KeySurface.All.Any(s => s.Id == package.SurfaceId)) SetKeySurface(package.SurfaceId);
        else Regenerate();

        // Importing a layout for a desk you do not currently have is allowed -
        // it will apply when that display appears - but saying nothing would
        // look exactly like a failed import.
        return preview.AnythingApplies
            ? null
            : "Imported, but none of it applies to the displays attached right now. " +
              $"It was made for: {string.Join(", ", preview.Missing)}.";
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
            _displays, surface, _config.General.Shape.ToTuning(),
            _config.General.AllowSpanningUnions, _config.Overrides);

        _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
        PersistCurrentLayout();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Come back elevated, and come back visible.
    /// <para>
    /// --show rather than --tray: this is only ever reached by pressing a button
    /// in a window that is open, so the app vanishing into the tray looks like
    /// the restart failed. It was --tray for a while with no visible effect,
    /// because the flag itself was being overruled at startup and every launch
    /// showed its window regardless.
    /// </para>
    /// </summary>
    public void RestartElevated()
    {
        if (Elevation.TryRestartElevated("--show")) Environment.Exit(0);
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

        _engine.BeginCapture((mods, key) => Dispatcher.UIThread.Post(() =>
        {
            completed(ApplyRebind(row, col, mods, key, surface));
        }),

            // Escape. It never reaches the UI - a capture is armed, so the hook
            // takes it first and swallows it - which is why the state machine
            // has to say so rather than the window listening for a key.
            () => Dispatcher.UIThread.Post(
                () => completed(new RebindResult(false, string.Empty, Canceled: true))));
    }

    public void CancelRebind() => _engine?.EndCapture();

    private RebindResult ApplyRebind(
        int row, int col, ChordModifiers mods, KeyStroke key, KeySurface surface)
    {
        if (_layout is null) return new RebindResult(false, "No layout.");

        // Any modifier will do, not just the configured default: that setting
        // is where UNBOUND zones get their chord from, and binding one by hand
        // is precisely the case for wanting something else. What is refused is
        // a bare key, which would fire while typing.
        if (mods == ChordModifiers.None)
        {
            return new RebindResult(false,
                "Hold at least one modifier - Win, Ctrl, Alt or Shift - while pressing the key.");
        }

        if (!KeyNames.IsBindable(key))
        {
            return new RebindResult(false,
                "That key cannot be used for a hotkey. Escape cancels, and a modifier " +
                "on its own is half a chord rather than a key.");
        }

        // Recorded as its own chord only when it differs from the default, so a
        // zone left on the default still follows it if the default changes.
        var chosen = mods == HotkeyModifier ? (ChordModifiers?)null : mods;

        // A key that IS on the surface moves the zone to that cell, which keeps
        // the grid describing the desk. Anything else stays where it is and
        // simply answers to a different key - the surface is a set of defaults,
        // not a list of the only keys allowed.
        var onSurface = key.Extended ? null : LayoutEditor.PositionOfScanCode(surface, key.ScanCode);

        var outcome = onSurface is not null
            ? LayoutEditor.Rebind(_layout, new GridPos(row, col), onSurface.Value, chosen, HotkeyModifier)
            : LayoutEditor.RebindToKey(_layout, new GridPos(row, col), key, chosen, HotkeyModifier);

        if (!outcome.Success) return new RebindResult(false, outcome.Message);

        _layout = outcome.Layout;

        // The one edit in the editor that was not recorded, so Undo stayed grey
        // after it and there was no way back from a key you had just changed.
        // The history carries the keys now, so this is all it takes.
        _history.Record(Now);

        _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
        PersistCurrentLayout();
        StateChanged?.Invoke();

        return new RebindResult(true, outcome.Message);
    }

    public MonitorDiagramViewModel BuildInteractiveDiagram(
        Action<GridPos>? onZoneActivated,
        Action<string, IReadOnlyList<double>>? onSplitChanged,
        Action<string, int>? onZoneCountChanged,
        Action<string, int>? onSubzoneAxisFlipped) =>
        MonitorDiagramViewModel.Build(
            _displays, _layout, onZoneActivated,
            onSplitChanged, onZoneCountChanged, onSubzoneAxisFlipped,
            _config.General.Shape.ToTuning(), _config.General.SnapSplits, HotkeyModifier);

    /// <summary>
    /// Turn one zone's subzones through a right angle and pin them there.
    /// <para>
    /// Written as the axis it is NOT currently using rather than as a toggle
    /// flag, so the stored value keeps meaning the same thing when the zone is
    /// later resized into a shape the rule would answer differently for. A toggle
    /// would silently invert itself the first time that happened.
    /// </para>
    /// </summary>
    public void FlipSubzoneAxis(string slot, int zone)
    {
        if (_layout is null || zone < 0) return;

        var display = _displays.FirstOrDefault(d => DisplaySlot.Of(d) == slot);
        if (display is null) return;

        var now = CurrentSubzoneAxis(display, zone);
        if (now is null) return;

        var wanted = now == Axis.Vertical ? Axis.Horizontal : Axis.Vertical;

        var existing = _config.Overrides.FirstOrDefault(o => o.Slot == slot)
                       ?? new DisplayOverride(slot);

        var axes = new List<string?>(existing.SubzoneAxes ?? []);
        while (axes.Count <= zone) axes.Add(null);
        axes[zone] = DisplayOverride.Word(wanted);

        var updated = existing with { SubzoneAxes = axes };

        _config = _config with
        {
            Overrides = [.. _config.Overrides.Where(o => o.Slot != slot), updated],
        };

        Regenerate();
        Save();
        _history.Record(Now);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Which way a zone's subzones are cut right now - read off the layout rather
    /// than recomputed, so a hand-pinned axis flips from where it actually is.
    /// </summary>
    private Axis? CurrentSubzoneAxis(DisplayInfo display, int zone)
    {
        var whole = _layout!.Zones
            .Where(z => z.Parts.Any(p => p.DisplayKey == display.StableKey))
            .Where(z => SubzoneFlip.Of(z, _layout) is null)
            .OrderBy(z => z.Parts[0].Area.X)
            .ThenBy(z => z.Parts[0].Area.Y)
            .Skip(zone)
            .FirstOrDefault();

        if (whole is null) return null;

        var half = _layout.Zones.FirstOrDefault(z =>
            !ReferenceEquals(z, whole) &&
            SubzoneFlip.Of(z, _layout) is not null &&
            z.Parts[0].DisplayKey == display.StableKey &&
            Inside(z.Parts[0].Area, whole.Parts[0].Area));

        if (half is null) return null;

        // Keeping the parent's full width means the pair is stacked.
        return Math.Abs(half.Parts[0].Area.W - whole.Parts[0].Area.W) <= 1e-6
            ? Axis.Vertical
            : Axis.Horizontal;
    }

    private static bool Inside(NormRect inner, NormRect outer) =>
        inner.X >= outer.X - 1e-6 && inner.Y >= outer.Y - 1e-6 &&
        inner.Right <= outer.Right + 1e-6 && inner.Bottom <= outer.Bottom + 1e-6;

    public void ResetLayout()
    {
        if (_displays.Count == 0) return;

        var surface = KeySurface.All.FirstOrDefault(s => s.Id == _layout?.Surface.Id) ?? KeySurface.LeftHandBlock;

        var fresh = LayoutBuilder.Build(
            _displays, surface, _config.General.Shape.ToTuning(),
            _config.General.AllowSpanningUnions, _config.Overrides);

        // Taken as it comes, with no keys carried over: this is the one place
        // that is meant to throw them away. Everywhere else rebuilds and puts
        // them back, which is the difference between regenerating and
        // resetting - and for a while these two methods were the same code.
        _layout = fresh;

        // Recorded, so resetting the keys can be undone like any other edit.
        // It throws away every rebind at once, which is the change most worth
        // being able to take back.
        _history.Record(Now);

        _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
        PersistCurrentLayout();
        StateChanged?.Invoke();
    }

    private void Regenerate() => RegenerateFrom(_layout);

    private void RegenerateFrom(LayoutResult? keysFrom)
    {
        if (_displays.Count == 0) return;

        var surface = KeySurface.All.FirstOrDefault(s => s.Id == _layout?.Surface.Id) ?? KeySurface.LeftHandBlock;

        var fresh = LayoutBuilder.Build(
            _displays, surface, _config.General.Shape.ToTuning(),
            _config.General.AllowSpanningUnions, _config.Overrides);

        // The builder is told about displays, a surface and zone shapes, and is
        // never told which chord a zone was bound to - so without this every
        // rebuild hands back the allocator's own answer and a rebind lasts only
        // until the next zone is resized, counted or undone.
        //
        // The keys come from the caller rather than from _layout, because undo
        // has to rebuild the shapes and then put back the keys as they were
        // BEFORE the edit, not as they stand now.
        _layout = LayoutEditor.CarryKeysOver(fresh, keysFrom);

        _engine?.Apply(_layout, _displays, HotkeyModifier, Actions);
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

        // Held back until the edit session is confirmed. Cancelling then needs
        // to restore nothing on disk, because nothing reached it.
        if (_editing)
        {
            _writePending = true;
            return;
        }

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

