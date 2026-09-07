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
        ChordModifiers modifiers = ChordModifiers.Win)
    {
        var bindings = new Dictionary<(ChordModifiers, ushort), HotkeyAction>();
        var rings = new Dictionary<(ChordModifiers, ushort), IReadOnlyList<HotkeyAction>>();
        var zones = new Dictionary<string, (GridPos, string, PxRect)>();

        foreach (var zone in layout.Zones)
        {
            var scan = layout.Surface.ScanCodeAt(zone.Position);

            // Its own modifier where it has been given one, the default
            // otherwise - so changing the default moves everything that has
            // not been bound by hand, and nothing that has.
            var chord = zone.ChordWith(modifiers);

            // Each ring step gets its own id so the executor can resolve which
            // rectangle a given press meant.
            var steps = RingBuilder.Build(zone, layout, displays);
            var actions = new List<HotkeyAction>(steps.Count);

            for (var i = 0; i < steps.Count; i++)
            {
                var id = $"{chord}:{zone.Position.Row}:{zone.Position.Col}:{i}";
                zones[id] = (zone.Position, steps[i].Name, Project(steps[i].Parts, displays));
                actions.Add(new HotkeyAction(id, zone.Position));
            }

            bindings[(chord, scan)] = actions[0];
            if (actions.Count > 1) rings[(chord, scan)] = actions;
        }

        // Fixed actions live OUTSIDE the key surface on purpose: the allocator
        // may claim any surface key when the display arrangement changes, so an
        // action bound there would silently stop working.
        foreach (var (scan, command) in FixedActions)
        {
            if (bindings.ContainsKey((modifiers, scan))) continue;
            bindings[(modifiers, scan)] = new HotkeyAction(command, null, command);
        }

        _zones = zones;
        _machine.SetBindings(bindings, rings);
    }

    private const ushort ScanBackspace = 0x0E;

    private static readonly (ushort Scan, string Command)[] FixedActions =
    [
        (ScanBackspace, CommandUndo),
    ];

    public const string CommandUndo = "undo";

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
    public event Action<ChordModifiers, ushort>? ChordCaptured;

    private Action<ChordModifiers, ushort>? _captureHandler;

    public void BeginCapture(Action<ChordModifiers, ushort> onCaptured)
    {
        EndCapture();

        _captureHandler = onCaptured;
        _machine.ChordCaptured += OnChordCaptured;
        _machine.BeginCapture();
    }

    public void EndCapture()
    {
        _machine.EndCapture();
        _machine.ChordCaptured -= OnChordCaptured;
        _captureHandler = null;
    }

    private void OnChordCaptured(ChordModifiers mods, ushort scan)
    {
        var handler = _captureHandler;

        // One capture per request: leaving it armed would swallow every
        // subsequent keystroke.
        EndCapture();

        handler?.Invoke(mods, scan);
        ChordCaptured?.Invoke(mods, scan);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _hook.Dispose();
        _cts.Dispose();
    }
}
