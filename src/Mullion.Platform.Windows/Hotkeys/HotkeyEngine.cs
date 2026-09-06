using System.Runtime.Versioning;
using Mullion.Core.Abstractions;
using Mullion.Core.Geometry;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;

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

    /// <summary>Bind a layout, resolving every zone to absolute pixels once.</summary>
    public void Apply(
        LayoutResult layout,
        IReadOnlyList<DisplayInfo> displays,
        ChordModifiers modifiers = ChordModifiers.Win)
    {
        var bindings = new Dictionary<(ChordModifiers, ushort), HotkeyAction>();
        var zones = new Dictionary<string, (GridPos, string, PxRect)>();

        foreach (var zone in layout.Zones)
        {
            var scan = layout.Surface.ScanCodeAt(zone.Position);
            var id = $"{zone.Position.Row}:{zone.Position.Col}";

            var rect = PxRect.Union(zone.Parts.Select(p =>
            {
                var display = displays.First(d => d.StableKey == p.DisplayKey);
                return p.Area.Project(display.WorkArea);
            }));

            bindings[(modifiers, scan)] = new HotkeyAction(id, zone.Position);
            zones[id] = (zone.Position, zone.Name, rect);
        }

        _zones = zones;
        _machine.SetBindings(bindings);
    }

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

    public void Dispose()
    {
        _cts.Cancel();
        _hook.Dispose();
        _cts.Dispose();
    }
}
