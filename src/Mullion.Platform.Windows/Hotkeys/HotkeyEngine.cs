using System.Runtime.Versioning;
using Mullion.Core.Abstractions;
using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Mullion.Platform.Windows.Windows;

namespace Mullion.Platform.Windows.Hotkeys;

public sealed record HotkeyFired(GridPos Position, string ZoneName, MoveResult Result);

/// <summary>
/// Binds a layout to the keyboard and executes matched hotkeys.
/// <para>
/// The executor runs on its own thread draining the hook's channel. It must
/// never run on the hook thread: moving a window makes blocking Win32 calls and
/// waits for the window to settle, which would blow the low-level hook timeout
/// and get the hook silently uninstalled mid-use.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyEngine : IDisposable
{
    private readonly HotkeyStateMachine _machine;
    private readonly LowLevelKeyboardHook _hook;
    private readonly IWindowManager _windows;
    private readonly CancellationTokenSource _cts = new();

    private Dictionary<string, (GridPos Position, string Name, PxRect Target)> _zones = [];
    private Task? _executor;

    public HotkeyEngine(
        IWindowManager windows,
        WinKeySuppression suppression = WinKeySuppression.DummyKey)
    {
        _windows = windows;
        _machine = new HotkeyStateMachine(suppression);
        _hook = new LowLevelKeyboardHook(_machine);
        _hook.Diagnostic += m => Diagnostic?.Invoke(m);
    }

    public event Action<HotkeyFired>? Fired;

    public event Action<string>? Diagnostic;

    public bool Paused
    {
        get => !_machine.Enabled;

        // Pausing does not unhook: keeping the hook warm avoids a reinstall
        // storm and keeps latency statistics flowing. The callback checks a
        // single bool and returns in nanoseconds.
        set => _machine.Enabled = !value;
    }

    public HookHealth Health => _hook.GetHealth();

    /// <summary>
    /// Stand down while something is running fullscreen. Default true, matching
    /// the config default - which previously claimed this behavior without
    /// implementing it.
    /// </summary>
    public bool PauseWhenFullscreen { get; set; } = true;

    /// <summary>
    /// Changeable at runtime: the point of offering alternatives is that a user
    /// whose Start menu misbehaves can try another without restarting.
    /// </summary>
    public WinKeySuppression Suppression
    {
        get => _machine.Suppression;
        set => _machine.Suppression = value;
    }

    /// <summary>Bind a layout, resolving every zone to absolute pixels once.</summary>
    public void Apply(
        LayoutResult layout,
        IReadOnlyList<DisplayInfo> displays,
        ChordModifiers modifiers = ChordModifiers.Win,
        IReadOnlyList<GlobalAction>? actions = null)
    {
        var bindings = new Dictionary<(ChordModifiers, KeyStroke), HotkeyAction>();
        var rings = new Dictionary<(ChordModifiers, KeyStroke), IReadOnlyList<HotkeyAction>>();
        var zones = new Dictionary<string, (GridPos, string, PxRect)>();

        foreach (var zone in layout.Zones)
        {
            // Its own key where it has been given one, the surface's otherwise.
            var scan = zone.KeyOn(layout.Surface);

            // Its own modifier where it has been given one, the default
            // otherwise - so changing the default moves everything that has
            // not been bound by hand, and nothing that has.
            var chord = zone.ChordWith(modifiers);

            // Each ring step gets its own id so the executor can resolve which
            // rectangle a given press meant.
            var steps = RingBuilder.Build(zone, layout, displays);
            var ringActions = new List<HotkeyAction>(steps.Count);

            for (var i = 0; i < steps.Count; i++)
            {
                var id = $"{chord}:{zone.Position.Row}:{zone.Position.Col}:{i}";
                zones[id] = (zone.Position, steps[i].Name, Project(steps[i].Parts, displays));
                ringActions.Add(new HotkeyAction(id, zone.Position));
            }

            bindings[(chord, scan)] = ringActions[0];
            if (ringActions.Count > 1) rings[(chord, scan)] = ringActions;

            // The same key with Shift held gives the half this one would have
            // been had its zone been cut the other way. Derived here rather than
            // built as zones, because the layout addresses one zone per grid
            // position and everything downstream of it relies on that; the
            // binding table is keyed by chord AND scan code, so a second entry
            // on the same key costs nothing and collides with nothing.
            //
            // Skipped where the chord already carries Shift: Win+Shift+A would
            // otherwise be both a zone's own chord and another zone's flip, and
            // the one that lost would do so silently.
            if (chord.HasFlag(ChordModifiers.Shift)) continue;

            var flipped = SubzoneFlip.Of(zone, layout);
            if (flipped is null) continue;

            var flipChord = chord | ChordModifiers.Shift;
            if (bindings.ContainsKey((flipChord, scan))) continue;

            var flipId = $"{flipChord}:{zone.Position.Row}:{zone.Position.Col}:flip";
            zones[flipId] = (zone.Position, OtherWay(zone.Name), Project(flipped, displays));
            bindings[(flipChord, scan)] = new HotkeyAction(flipId, zone.Position);
        }

        // Actions live OUTSIDE the key surface on purpose: the allocator may
        // claim any surface key when the display arrangement changes, so an
        // action bound there would silently stop working. A zone that has been
        // moved onto an action's key wins, because that one was chosen by hand
        // just now and this one may only be a default nobody has looked at.
        foreach (var action in actions ?? GlobalAction.Defaults)
        {
            var chord = action.ChordWith(modifiers);
            if (bindings.ContainsKey((chord, action.Key))) continue;

            bindings[(chord, action.Key)] = new HotkeyAction(action.Command, null, action.Command);
        }

        _zones = zones;
        _machine.SetBindings(bindings, rings);
    }

    /// <summary>
    /// The same zone name with its direction turned through a right angle, for
    /// the "Last action" line. Naming it "Left upper" when it just put the window
    /// on the left half would describe the key rather than what happened.
    /// </summary>
    private static string OtherWay(string name)
    {
        foreach (var (from, to) in Opposites)
        {
            if (name.EndsWith(from, StringComparison.Ordinal))
                return string.Concat(name.AsSpan(0, name.Length - from.Length), to);
        }

        return name;
    }

    private static readonly (string From, string To)[] Opposites =
    [
        (" upper", " left"),
        (" lower", " right"),
        (" left", " upper"),
        (" right", " lower"),
    ];

    public const string CommandUndo = GlobalAction.Undo;

    public const string CommandMinimize = GlobalAction.Minimize;

    private static PxRect Project(IReadOnlyList<ZonePart> parts, IReadOnlyList<DisplayInfo> displays) =>
        PxRect.Union(parts.Select(p =>
        {
            var display = displays.First(d => d.StableKey == p.DisplayKey);
            return p.Area.Project(display.WorkArea);
        }));

    public void Start()
    {
        _hook.Start();
        _executor ??= Task.Run(ExecuteLoop);
    }

    private async Task ExecuteLoop()
    {
        try
        {
            await foreach (var action in _hook.Actions.ReadAllAsync(_cts.Token))
            {
                // Checked here, off the hook thread, so it costs nothing on the
                // input path. Moving a window into a fullscreen game is worse
                // than doing nothing: it can drop the game out of exclusive
                // mode, or resize something the user cannot even see.
                if (PauseWhenFullscreen && FullscreenDetector.IsFullscreenActive(out var why))
                {
                    Diagnostic?.Invoke($"Ignored {action.Id}: {why}.");
                    continue;
                }

                if (action.Command == CommandUndo)
                {
                    var undone = _windows.UndoLastMove();
                    Diagnostic?.Invoke(undone ? "Undid the last move." : "Nothing to undo.");
                    continue;
                }

                if (action.Command == CommandMinimize)
                {
                    var minimized = _windows.MinimizeForeground();
                    Diagnostic?.Invoke(minimized
                        ? "Minimized the focused window."
                        : "Nothing eligible to minimize.");
                    continue;
                }

                if (!_zones.TryGetValue(action.Id, out var zone)) continue;

                var result = _windows.MoveForegroundTo(zone.Target);
                Fired?.Invoke(new HotkeyFired(zone.Position, zone.Name, result));

                if (result.Note is not null) Diagnostic?.Invoke(result.Note);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Recover modifier state after a session unlock, resume or re-hook.</summary>
    public void Resync() => _machine.ResyncModifiers(LowLevelKeyboardHook.ReadPhysicalModifiers());

    /// <summary>
    /// Raised when a chord is captured, with the physical scan code.
    /// <para>
    /// Capture has to run through the hook because the UI framework never
    /// receives Win-modified keys - the shell takes them first. This is the only
    /// way a "press a key" control can see Win+A at all.
    /// </para>
    /// </summary>
    public event Action<ChordModifiers, KeyStroke>? ChordCaptured;

    private Action<ChordModifiers, KeyStroke>? _captureHandler;
    private Action? _cancelHandler;

    public void BeginCapture(Action<ChordModifiers, KeyStroke> onCaptured, Action? onCanceled = null)
    {
        EndCapture();

        _captureHandler = onCaptured;
        _cancelHandler = onCanceled;
        _machine.ChordCaptured += OnChordCaptured;
        _machine.CaptureCanceled += OnCaptureCanceled;
        _machine.BeginCapture();
    }

    public void EndCapture()
    {
        _machine.EndCapture();
        _machine.ChordCaptured -= OnChordCaptured;
        _machine.CaptureCanceled -= OnCaptureCanceled;
        _captureHandler = null;
        _cancelHandler = null;
    }

    private void OnCaptureCanceled()
    {
        var handler = _cancelHandler;

        EndCapture();

        handler?.Invoke();
    }

    private void OnChordCaptured(ChordModifiers mods, KeyStroke key)
    {
        var handler = _captureHandler;

        // One capture per request: leaving it armed would swallow every
        // subsequent keystroke.
        EndCapture();

        handler?.Invoke(mods, key);
        ChordCaptured?.Invoke(mods, key);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _hook.Dispose();
        _cts.Dispose();
    }
}
