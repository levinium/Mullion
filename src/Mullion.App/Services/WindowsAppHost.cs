#if PLATFORM_WINDOWS
using Avalonia.Threading;
using Mullion.App.ViewModels;
using Mullion.Core.Config;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Mullion.Platform.Windows.Displays;
using Mullion.Platform.Windows.Hotkeys;
using Mullion.Platform.Windows.Windows;

namespace Mullion.App.Services;

/// <summary>Wires the Windows platform pieces together and exposes them to the UI.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsAppHost : IAppHost, IWizardHost, IDisposable
{
    private readonly WindowsDisplayProvider _displayProvider = new();
    private readonly ConfigStore _configStore = new();
    private readonly WindowManager _windows = new();

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

    public void Start()
    {
        _config = _configStore.Load();

        _engine = new HotkeyEngine(_windows, ParseSuppression(_config.General.WinKeySuppression));
        _engine.Fired += OnFired;

        Rescan();
        _engine.Start();

        // Rescan raised StateChanged before the hook existed, so the UI would
        // otherwise sit showing "Not installed" until something else changed.
        StateChanged?.Invoke();
    }

    public void Rescan()
    {
        _displays = _displayProvider.GetDisplays();
        if (_displays.Count == 0) return;

        var resolution = ProfileResolver.Resolve(_config, _displays);

        // A stored profile is reused when the displays are recognisable, so a
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

        if (resolution.Match == ProfileMatch.None)
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

    private void OnFired(HotkeyFired fired)
    {
        _lastAction = $"{fired.ZoneName} — {fired.Result.Outcome}";
        if (fired.Result.Note is not null) _lastAction += $" ({fired.Result.Note})";

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

        var privilege = Elevation.IsCurrentProcessElevated
            ? "Running elevated — windows that run as administrator can be managed."
            : "Not elevated — hotkeys will not fire while a window running as administrator has focus.";

        var summary = _displays.Count == 0
            ? "No displays detected"
            : $"{_displays.Count} display{(_displays.Count == 1 ? "" : "s")} · " +
              string.Join(" · ", _displays.Select(d => $"{d.Bounds.Width}×{d.Bounds.Height}"));

        return new AppSnapshot(
            MonitorDiagramViewModel.Build(_displays, _layout),
            summary,
            hookStatus,
            privilege,
            Paused,
            _lastAction,
            _conflicts);
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

    public void Dispose() => _engine?.Dispose();
}
#endif
