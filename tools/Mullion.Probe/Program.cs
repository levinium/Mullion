using Mullion.Core.Abstractions;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Mullion.Platform.Windows.Displays;
using Mullion.Platform.Windows.Windows;

// Diagnostic harness. Everything here is verifiable without any UI or hotkey
// machinery in the way, which is what makes the pipeline testable early.
//
//   (no args)            print the detected topology and derived layout
//   --snap <KEY> [--delay N]   snap the foreground window to that zone
//   --undo                     restore the last move
//   --toggle-test              verify the Shift+drag size toggle end to end

WindowsDisplayProvider.EnsurePerMonitorDpiAwareness();

var provider = new WindowsDisplayProvider();
var displays = provider.GetDisplays();
var surface = KeySurface.LeftHandBlock;
var layout = LayoutBuilder.Build(displays, surface);

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (argv.Contains("--undo"))
{
    var mover = new WindowManager();
    Console.WriteLine(mover.UndoLastMove() ? "Undone." : "Nothing to undo (undo stack is per-process).");
    return 0;
}

if (argv.Contains("--self-test"))
{
    // Launch a real window and drive it through every zone, reporting how far
    // each landed from its target. This is the end-to-end check on the mover:
    // extended-frame-bounds correction, restore-before-move, and verify/retry.
    using var scratch = new Mullion.Platform.Windows.Testing.ScratchWindow();
    var hwnd = scratch.Handle;
    Thread.Sleep(300);

    var mover = new WindowManager();
    var failures = 0;

    Console.WriteLine($"Driving scratch window (hwnd 0x{hwnd:X}) through {layout.Zones.Count} zones");
    Console.WriteLine();
    Console.WriteLine($"  {"key",-5} {"outcome",-18} {"target",-26} {"achieved",-26} delta");

    foreach (var z in layout.Zones.OrderBy(z => z.Position.Row).ThenBy(z => z.Position.Col))
    {
        var t = ProjectZone(z);
        var r = mover.MoveWindowTo(hwnd, t);

        var dx = Math.Abs(r.Achieved.Left - t.Left);
        var dy = Math.Abs(r.Achieved.Top - t.Top);
        var dw = Math.Abs(r.Achieved.Width - t.Width);
        var dh = Math.Abs(r.Achieved.Height - t.Height);
        var worst = Math.Max(Math.Max(dx, dy), Math.Max(dw, dh));

        if (worst > 2) failures++;

        Console.WriteLine(
            $"  {surface.FallbackLabelAt(z.Position),-5} {r.Outcome,-18} {t,-26} {r.Achieved,-26} " +
            $"{(worst <= 2 ? "ok" : $"OFF BY {worst}px")}");

        if (r.Note is not null) Console.WriteLine($"        note: {r.Note}");
    }

    Console.WriteLine();
    Console.WriteLine($"Undo stack depth: {mover.UndoDepth}");
    Console.WriteLine(mover.UndoLastMove() ? "Undo: restored." : "Undo: nothing to restore.");

    Console.WriteLine();
    Console.WriteLine(failures == 0
        ? "All zones landed within 2px."
        : $"{failures} zone(s) missed by more than 2px.");

    return failures == 0 ? 0 : 1;
}

var injectIndex = Array.IndexOf(argv, "--inject");
if (injectIndex >= 0 && injectIndex + 1 < argv.Length)
{
    // Inject a Win+key chord WITHOUT installing a hook of our own, so only an
    // already-running Mullion can respond. This is how the app itself gets
    // tested rather than the probe testing its own engine.
    var key = argv[injectIndex + 1].ToUpperInvariant()[0];
    if (key is < 'A' or > 'Z') { Console.Error.WriteLine("Expected a letter."); return 1; }

    var vk = (ushort)key;
    ushort[] scans =
    [
        0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
        0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C,
    ];

    using var target = new Mullion.Platform.Windows.Testing.ScratchWindow($"Mullion target ({key})");
    target.Focus();
    Thread.Sleep(500);

    var before = Mullion.Platform.Windows.Testing.KeyInjector.ForegroundWindow();

    Mullion.Platform.Windows.Testing.KeyInjector.Chord(
        Mullion.Platform.Windows.Testing.KeyInjector.VkLWin, vk, scans[key - 'A']);

    Thread.Sleep(900);

    var after = Mullion.Platform.Windows.Testing.KeyInjector.ForegroundWindow();
    Console.WriteLine($"Injected Win+{key} at the running app.");

    // Naming the process that holds focus matters: "focus moved" on its own
    // cannot distinguish the Start menu opening from this console window
    // simply taking focus back, and those mean opposite things.
    var owner = Mullion.Platform.Windows.Testing.KeyInjector.ForegroundProcessName();
    var startMenu = owner is "StartMenuExperienceHost" or "SearchHost" or "ShellExperienceHost";

    Console.WriteLine($"  foreground now: {owner}");
    Console.WriteLine(startMenu
        ? "  START MENU OPENED - suppression failed."
        : after == before || after == target.Handle
            ? "  focus stayed with the target window."
            : "  focus moved, but not to the shell - suppression held.");

    return startMenu ? 1 : 0;
}

if (argv.Contains("--watch-displays"))
{
    using var watcher = new Mullion.Platform.Windows.Displays.DisplayChangeWatcher(
        TimeSpan.FromMilliseconds(400));

    Console.WriteLine($"watcher hwnd: 0x{watcher.Handle:X}");
    Console.WriteLine("Listening. Change a display setting, move the taskbar, or plug a monitor.");
    Console.WriteLine();

    watcher.MessageObserved += m => Console.WriteLine($"  [msg]     {m}");
    watcher.Changed += () => Console.WriteLine($"  [CHANGED] arrangement settled at {DateTime.Now:HH:mm:ss.fff}");

    var seconds = 30;
    var forIdx = Array.IndexOf(argv, "--for");
    if (forIdx >= 0 && forIdx + 1 < argv.Length && int.TryParse(argv[forIdx + 1], out var s)) seconds = s;

    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    Console.WriteLine("done");
    return 0;
}

if (argv.Contains("--undo-test"))
{
    // Undo was implemented but unreachable: nothing in the app bound it.
    // This drives it through the hook the way a user now can.
    using var scratch = new Mullion.Platform.Windows.Testing.ScratchWindow("Mullion undo test");
    scratch.Focus();
    Thread.Sleep(400);

    var mover = new WindowManager();
    using var engine = new Mullion.Platform.Windows.Hotkeys.HotkeyEngine(mover);
    engine.Apply(layout, displays);
    engine.Diagnostic += m => Console.WriteLine($"  [diag] {m}");
    engine.Start();
    Thread.Sleep(300);

    var before = mover.MoveForegroundTo(ProjectZone(layout.At(1, 0)!));
    Console.WriteLine($"  placed left : {before.Achieved}");

    scratch.Focus();
    Thread.Sleep(250);

    Console.WriteLine("  pressing Win+Backspace…");
    Mullion.Platform.Windows.Testing.KeyInjector.Chord(
        Mullion.Platform.Windows.Testing.KeyInjector.VkLWin, 0x08, 0x0E);

    Thread.Sleep(900);
    Console.WriteLine("  (undo restores the pre-move placement)");
    return 0;
}

if (argv.Contains("--elevation-check"))
{
    var self = Mullion.Platform.Windows.Windows.Elevation.IsCurrentProcessElevated;
    Console.WriteLine($"Mullion elevated : {self}");
    Console.WriteLine($"Mullion integrity: {Mullion.Platform.Windows.Windows.Elevation.CurrentIntegrity}");
    Console.WriteLine();

    // A specific window can be named; otherwise take whatever has focus.
    var raw = Environment.GetEnvironmentVariable("MULLION_TEST_HWND");
    var hwnd = long.TryParse(raw, out var parsed) && parsed != 0
        ? (nint)parsed
        : Mullion.Platform.Windows.Testing.KeyInjector.ForegroundWindow();

    if (hwnd == 0) { Console.Error.WriteLine("No target window."); return 1; }

    var integrity = Mullion.Platform.Windows.Windows.Elevation.IntegrityOfWindow(hwnd);
    var outOfReach = Mullion.Platform.Windows.Windows.Elevation.IsOutOfReach(hwnd);

    Console.WriteLine($"Target window    : 0x{hwnd:X}");
    Console.WriteLine($"Target integrity : {integrity}");
    Console.WriteLine($"Out of reach     : {outOfReach}");
    Console.WriteLine();

    var target = ProjectZone(layout.At(1, 0)!);
    var result = new WindowManager().MoveWindowTo(hwnd, target);

    Console.WriteLine($"Move outcome     : {result.Outcome}");
    Console.WriteLine($"Achieved         : {result.Achieved}");
    if (result.Note is not null) Console.WriteLine($"Note             : {result.Note}");

    // Success here means the move matched what the integrity check predicted,
    // whichever way round that is.
    var consistent = outOfReach
        ? result.Outcome == MoveOutcome.FailedAccessDenied
        : result.Success;

    Console.WriteLine();
    Console.WriteLine(consistent
        ? "Prediction and outcome agree."
        : "MISMATCH - the integrity check disagrees with what actually happened.");

    return consistent ? 0 : 1;
}

if (argv.Contains("--rebind-survives"))
{
    // Does a key rebound to a non-default modifier survive the next time the
    // layout is regenerated? Regenerate() rebuilds from LayoutBuilder, which is
    // given displays, a surface, tuning, unions and zone overrides - and knows
    // nothing about which chord a zone was bound to. This says out loud what
    // that costs.
    var before = LayoutEditor.Rebind(
        layout,
        layout.Zones[0].Position,
        layout.Zones[0].Position,
        Mullion.Core.Hotkeys.ChordModifiers.Control | Mullion.Core.Hotkeys.ChordModifiers.Shift);

    Console.WriteLine($"Rebound {layout.Zones[0].Name}: {before.Message}");
    Console.WriteLine($"  modifier now : {before.Layout.At(layout.Zones[0].Position)!.Modifier?.ToString() ?? "(default)"}");
    Console.WriteLine();

    // What the builder alone answers. It is given displays, a surface, tuning,
    // unions and zone shapes, and has never been told which chord a zone was
    // bound to - so on its own it hands back the allocator's own answer.
    var rebuilt = LayoutBuilder.Build(displays, surface);

    Console.WriteLine("The builder on its own, which is all a regenerate used to do:");
    Console.WriteLine($"  modifier now : {rebuilt.At(layout.Zones[0].Position)!.Modifier?.ToString() ?? "(default - the rebind would be gone)"}");
    Console.WriteLine();

    // What the host does now: rebuild, then put the keys back.
    var carried = LayoutEditor.CarryKeysOver(rebuilt, before.Layout);
    var kept = carried.At(layout.Zones[0].Position)!.Modifier;

    Console.WriteLine("And with the keys carried over, which is what a zone count change,");
    Console.WriteLine("a seam drag, an undo and a display change all go through:");
    Console.WriteLine($"  modifier now : {kept?.ToString() ?? "(default)"}");
    Console.WriteLine();
    Console.WriteLine(kept is null
        ? "LOST. A rebind does not survive a regenerate."
        : "Kept. A rebind survives a regenerate.");

    return kept is null ? 1 : 0;
}

if (argv.Contains("--toggle-test"))
{
    // The Shift+drag toggle, end to end against a real window. It looked broken
    // because the remembered size was kept beside the drop handler, so a window
    // filled by a HOTKEY had nothing to come back from - and that is precisely
    // the sequence a person performs. Every step below is a real move through
    // the real window manager; nothing here simulates the thing under test.
    using var scratch = new Mullion.Platform.Windows.Testing.ScratchWindow("Mullion toggle test");
    var hwnd = scratch.Handle;
    Thread.Sleep(400);

    var mover = new WindowManager();
    var zone = ProjectZone(layout.At(1, 0)!);
    var failures = 0;

    Console.WriteLine($"Zone under test: {zone}");
    Console.WriteLine();

    void Check(string what, bool ok, string detail)
    {
        if (!ok) failures++;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what,-46} {detail}");
    }

    // One press of the toggle: read the window, ask the same rule the app asks,
    // move where it says. Calling ZoneFit.Plan rather than restating the rule is
    // the point - a probe with its own copy could pass while the app failed.
    bool Toggle(string label)
    {
        var current = mover.BoundsOf(hwnd);
        var plan = ZoneFit.Plan(current, mover.ChosenSizeOf(hwnd), zone);
        var result = mover.MoveWindowTo(hwnd, plan.Target);

        Console.WriteLine(
            $"        {label,-20} {(plan.Restoring ? "restore" : "fill   ")} -> {result.Achieved}");

        return plan.Restoring;
    }

    var original = mover.BoundsOf(hwnd)!.Value;
    Console.WriteLine($"  window starts at {original}");
    Console.WriteLine();

    // 1. Filled by hotkey, which is where the old implementation lost the size.
    Console.WriteLine("  filling the zone the way a hotkey does:");
    mover.MoveWindowTo(hwnd, zone);

    Check("the pre-move size is remembered",
        mover.ChosenSizeOf(hwnd) == original,
        $"{mover.ChosenSizeOf(hwnd)?.ToString() ?? "(none)"}");

    Check("the window fills the zone",
        ZoneFit.Fills(mover.BoundsOf(hwnd)!.Value, zone),
        $"{mover.BoundsOf(hwnd)}");

    // 2. Dropped into the same zone: the way back.
    Console.WriteLine();
    Console.WriteLine("  dropping it into the same zone:");
    var restored = Toggle("first drop");
    var back = mover.BoundsOf(hwnd)!.Value;

    Check("it comes back to its original size", restored &&
        Math.Abs(back.Width - original.Width) <= ZoneFit.Tolerance &&
        Math.Abs(back.Height - original.Height) <= ZoneFit.Tolerance,
        $"{back.Width}x{back.Height} vs {original.Width}x{original.Height}");

    // 3. And out again, because a toggle that only goes one way is a button.
    var refilled = Toggle("second drop");
    Check("dropping again fills the zone", !refilled &&
        ZoneFit.Fills(mover.BoundsOf(hwnd)!.Value, zone),
        $"{mover.BoundsOf(hwnd)}");

    // 4. The case the user described: resize it yourself, then use Mullion
    //    again. What comes back must be the size YOU last set, not the one
    //    Mullion remembered from before you touched it.
    Console.WriteLine();
    Console.WriteLine("  resizing it by hand, then filling the zone again:");
    var byHand = new Mullion.Core.Geometry.PxRect(zone.Left + 120, zone.Top + 90, 640, 480);
    scratch.ResizeByHand(byHand.Left, byHand.Top, byHand.Width, byHand.Height);
    Thread.Sleep(250);

    var seen = mover.BoundsOf(hwnd)!.Value;
    Console.WriteLine($"        resized by hand to  {seen}");

    mover.MoveWindowTo(hwnd, zone);

    var remembered = mover.ChosenSizeOf(hwnd);
    Check("the hand-set size replaced the old one",
        remembered is { } r &&
        Math.Abs(r.Width - seen.Width) <= ZoneFit.Tolerance &&
        Math.Abs(r.Height - seen.Height) <= ZoneFit.Tolerance,
        $"{remembered?.ToString() ?? "(none)"}");

    Check("and NOT the size from before that",
        remembered is { } r2 && Math.Abs(r2.Width - original.Width) > ZoneFit.Tolerance,
        $"original was {original.Width}x{original.Height}");

    Console.WriteLine();
    Console.WriteLine("  dropping into the same zone again:");
    Toggle("third drop");
    var afterHand = mover.BoundsOf(hwnd)!.Value;

    Check("it returns to the hand-set size",
        Math.Abs(afterHand.Width - seen.Width) <= ZoneFit.Tolerance &&
        Math.Abs(afterHand.Height - seen.Height) <= ZoneFit.Tolerance,
        $"{afterHand.Width}x{afterHand.Height} vs {seen.Width}x{seen.Height}");

    // 5. Moving between zones is not a resize by the user, so the remembered
    //    size must survive the trip.
    Console.WriteLine();
    Console.WriteLine("  sending it through another zone and back:");
    var other = ProjectZone(layout.Zones.Where(z => z.Position.Row == 1).MaxBy(z => z.Position.Col)!);
    mover.MoveWindowTo(hwnd, other);
    mover.MoveWindowTo(hwnd, zone);

    Check("the hand-set size survived the round trip",
        mover.ChosenSizeOf(hwnd) is { } r3 &&
        Math.Abs(r3.Width - seen.Width) <= ZoneFit.Tolerance,
        $"{mover.ChosenSizeOf(hwnd)?.ToString() ?? "(none)"}");

    // 6. The gesture as it is actually performed. Everything above drops the
    //    window without moving it first, which no real drag does - the window
    //    follows the pointer, so by the time it is let go it is nowhere near
    //    the zone it was picked up from.
    Console.WriteLine();
    Console.WriteLine("  picking it up, dragging it within the zone, and letting go:");
    mover.MoveWindowTo(hwnd, zone);
    var pickedUpAt = mover.BoundsOf(hwnd)!.Value;
    var wantedBack = mover.ChosenSizeOf(hwnd);

    scratch.DragByHand(340, 210);
    Thread.Sleep(250);

    var letGoAt = mover.BoundsOf(hwnd)!.Value;
    Console.WriteLine($"        picked up at        {pickedUpAt}");
    Console.WriteLine($"        let go at           {letGoAt}");

    Check("the drag really did move it off the zone",
        !ZoneFit.Fills(letGoAt, zone),
        "otherwise this step proves nothing");

    // The app asks about where the drag BEGAN, which is the whole point.
    var dragPlan = ZoneFit.Plan(pickedUpAt, wantedBack, zone);

    Check("dropping it on the zone it already fills restores",
        dragPlan.Restoring,
        $"target {dragPlan.Target}");

    Check("asking about the drop position instead would not",
        !ZoneFit.Plan(letGoAt, wantedBack, zone).Restoring,
        "which is how the gesture came to look dead");

    mover.MoveWindowTo(hwnd, dragPlan.Target);

    Check("and the remembered size survived the drag",
        mover.ChosenSizeOf(hwnd) is { } r4 && ZoneFit.SameSize(r4, wantedBack!.Value),
        $"{mover.ChosenSizeOf(hwnd)?.ToString() ?? "(none)"}");

    Console.WriteLine();
    Console.WriteLine(failures == 0
        ? "Toggle behaves as specified."
        : $"{failures} check(s) failed.");

    return failures == 0 ? 0 : 1;
}

if (argv.Contains("--drag-test"))
{
    // The open question drag-to-snap rests on: EVENT_SYSTEM_MOVESIZESTART only
    // fires for windows that use the standard modal move loop, and an app that
    // drags its own title bar never enters it. This says which of YOUR apps
    // report a drag, which is the only way to know whether the plain hook is
    // enough or a mouse hook is needed as well.
    var targets = DropTargets.Build(layout, displays);

    Console.WriteLine($"{targets.Count} drop targets:");
    foreach (var t in targets) Console.WriteLine($"  {t.Zone.Name,-16} {t.Bounds}");
    Console.WriteLine();
    Console.WriteLine("Drag some windows around. Hold SHIFT while dragging to see the");
    Console.WriteLine("target that would be used. Ctrl+C to stop.");
    Console.WriteLine();

    using var watcher = new DragWatcher();
    var lastZone = string.Empty;

    watcher.DragStarted += d =>
        Console.WriteLine($"START  {Describe(d.Hwnd)}  {d.Width}x{d.Height} at {d.X},{d.Y}");

    watcher.DragMoved += d =>
    {
        var shift = Modifiers.IsShiftDown;
        var hit = DropTargets.HitTest(targets, d.X, d.Y);
        var name = $"{shift}|{hit?.Zone.Name ?? "-"}";

        // Location changes arrive continuously; only report what changed.
        if (name == lastZone) return;
        lastZone = name;

        Console.WriteLine($"  over {hit?.Zone.Name ?? "(no zone)",-16} shift={shift}");
    };

    watcher.DragEnded += d =>
    {
        var shift = Modifiers.IsShiftDown;
        var hit = DropTargets.HitTest(targets, d.X, d.Y);
        lastZone = string.Empty;

        Console.WriteLine(
            $"END    {d.Width}x{d.Height} at {d.X},{d.Y}  shift={shift}  " +
            $"would snap to: {(shift ? hit?.Zone.Name ?? "(no zone)" : "(shift not held)")}");
        Console.WriteLine();
    };

    Thread.Sleep(Timeout.Infinite);
    return 0;

    // Naming the process is the whole point of this mode: the question is which
    // APPS report a drag, and an hwnd alone does not answer it.
    static string Describe(nint hwnd)
    {
        DragProbe.GetWindowThreadProcessId(hwnd, out var pid);
        try { return $"{System.Diagnostics.Process.GetProcessById((int)pid).ProcessName,-16} 0x{hwnd:X}"; }
        catch { return $"{"?",-16} 0x{hwnd:X}"; }
    }
}

if (argv.Contains("--fullscreen-check"))
{
    var active = Mullion.Platform.Windows.Windows.FullscreenDetector.IsFullscreenActive(out var why);
    Console.WriteLine($"fullscreen active: {active}{(why is null ? "" : $" — {why}")}");
    return 0;
}

if (argv.Contains("--cycle-test"))
{
    // Repeat-press cycling: the same key pressed again WHILE Win is still held
    // must widen the window, and releasing Win must reset to the first step.
    using var scratch = new Mullion.Platform.Windows.Testing.ScratchWindow("Mullion cycle test");
    scratch.Focus();
    Thread.Sleep(400);

    using var engine = new Mullion.Platform.Windows.Hotkeys.HotkeyEngine(new WindowManager());
    engine.Apply(layout, displays);

    var seen = new List<Mullion.Platform.Windows.Hotkeys.HotkeyFired>();
    engine.Fired += f => { lock (seen) seen.Add(f); };
    engine.Start();
    Thread.Sleep(300);

    Console.WriteLine("Holding Win and tapping A three times:");
    lock (seen) seen.Clear();

    Mullion.Platform.Windows.Testing.KeyInjector.Chord(
        Mullion.Platform.Windows.Testing.KeyInjector.VkLWin, 0x41, 0x1E, taps: 3);

    Thread.Sleep(900);

    List<Mullion.Platform.Windows.Hotkeys.HotkeyFired> held;
    lock (seen) held = [.. seen];

    foreach (var f in held) Console.WriteLine($"    {f.ZoneName,-42} {f.Result.Achieved}");

    var widened = held.Count >= 2 &&
                  held.Zip(held.Skip(1)).All(p => p.Second.Result.Achieved.Area >= p.First.Result.Achieved.Area);

    Console.WriteLine(widened ? "  ok - each press widened" : "  FAIL - did not widen");

    Console.WriteLine();
    Console.WriteLine("Releasing Win between presses (three separate chords):");
    lock (seen) seen.Clear();

    for (var i = 0; i < 3; i++)
    {
        Mullion.Platform.Windows.Testing.KeyInjector.Chord(
            Mullion.Platform.Windows.Testing.KeyInjector.VkLWin, 0x41, 0x1E);
        Thread.Sleep(500);
    }

    List<Mullion.Platform.Windows.Hotkeys.HotkeyFired> separate;
    lock (seen) separate = [.. seen];

    foreach (var f in separate) Console.WriteLine($"    {f.ZoneName,-42} {f.Result.Achieved}");

    var reset = separate.Count >= 2 &&
                separate.Select(f => f.Result.Achieved).Distinct().Count() == 1;

    Console.WriteLine(reset ? "  ok - reset to the first step each time" : "  FAIL - did not reset on release");

    return widened && reset ? 0 : 1;
}

if (argv.Contains("--hotkey-test"))
{
    // End-to-end proof that Win-modified hotkeys work natively: install the
    // hook, synthesise real Win+key events, and verify the window actually
    // moved to the right zone.
    using var scratch = new Mullion.Platform.Windows.Testing.ScratchWindow("Mullion hotkey test");
    scratch.Focus();
    Thread.Sleep(500);

    using var engine = new Mullion.Platform.Windows.Hotkeys.HotkeyEngine(new WindowManager());
    engine.Apply(layout, displays);

    var fired = new List<Mullion.Platform.Windows.Hotkeys.HotkeyFired>();
    engine.Fired += f => { lock (fired) fired.Add(f); };
    engine.Diagnostic += m => Console.WriteLine($"  [diag] {m}");

    engine.Start();
    Thread.Sleep(300);

    var health = engine.Health;
    Console.WriteLine($"Hook installed        : {health.Installed}");
    Console.WriteLine($"LowLevelHooksTimeout  : {health.LowLevelHooksTimeoutMs}ms");
    Console.WriteLine($"Running elevated      : {health.Elevated}");
    Console.WriteLine();

    if (!health.Installed) { Console.Error.WriteLine("Hook failed to install."); return 1; }

    // Keys PowerToys Keyboard Manager remaps on this machine (Win+A/S/D ->
    // Ctrl+Alt+1/2/3). Results for those are confounded: PowerToys would also
    // be suppressing the Start menu, so they prove nothing about our own
    // suppression. Marked so the output says so rather than implying a clean pass.
    var confounded = ReadPowerToysWinRemaps();

    var problems = 0;

    Console.WriteLine($"  {"key",-6} {"zone",-28} {"outcome",-9} {"start menu",-12} verdict");

    foreach (var (label, vk, scan) in new (string, ushort, ushort)[]
             {
                 ("A", 0x41, 0x1E), ("S", 0x53, 0x1F), ("D", 0x44, 0x20),
                 ("Q", 0x51, 0x10), ("W", 0x57, 0x11), ("E", 0x45, 0x12),
                 ("Z", 0x5A, 0x2C), ("X", 0x58, 0x2D), ("C", 0x43, 0x2E),
             })
    {
        scratch.Focus();
        Thread.Sleep(250);

        var before = Mullion.Platform.Windows.Testing.KeyInjector.ForegroundWindow();

        lock (fired) fired.Clear();
        Mullion.Platform.Windows.Testing.KeyInjector.Chord(
            Mullion.Platform.Windows.Testing.KeyInjector.VkLWin, vk, scan);

        Thread.Sleep(700);

        // If the Start menu opened it would steal foreground from the scratch
        // window. This is the only automated signal available for suppression.
        var after = Mullion.Platform.Windows.Testing.KeyInjector.ForegroundWindow();
        var startStoleFocus = after != before && after != scratch.Handle;

        Mullion.Platform.Windows.Hotkeys.HotkeyFired? hit;
        lock (fired) hit = fired.LastOrDefault();

        var note = confounded.Contains(label) ? "(PowerToys remaps this)" : "";

        if (hit is null)
        {
            Console.WriteLine($"  Win+{label,-2} {"-",-28} {"NO FIRE",-9} {"-",-12} FAIL {note}");
            problems++;
            continue;
        }

        var expected = layout.Zones.First(z => surface.FallbackLabelAt(z.Position) == label);
        var target = ProjectZone(expected);
        var placed = hit.Result.Success &&
                     Math.Abs(hit.Result.Achieved.Left - target.Left) <= 2 &&
                     Math.Abs(hit.Result.Achieved.Width - target.Width) <= 2;

        var ok = placed && !startStoleFocus;
        if (!ok) problems++;

        Console.WriteLine(
            $"  Win+{label,-2} {hit.ZoneName,-28} {hit.Result.Outcome,-9} " +
            $"{(startStoleFocus ? "OPENED" : "suppressed"),-12} {(ok ? "ok" : "FAIL")} {note}");
    }

    Console.WriteLine();
    if (confounded.Count > 0)
    {
        Console.WriteLine(
            $"  Note: PowerToys remaps Win+{string.Join("/", confounded)} on this machine. Those rows are");
        Console.WriteLine(
            "  not independent evidence - PowerToys suppresses the Start menu for them too.");
        Console.WriteLine(
            "  The unremapped keys above are the clean proof that native interception works.");
    }

    var final = engine.Health;
    Console.WriteLine();
    Console.WriteLine($"Reinstalls {final.ReinstallCount}   latency p50 {final.LatencyP50Ms:0.###}ms max {final.LatencyMaxMs:0.###}ms");
    Console.WriteLine(problems == 0 ? "Hotkeys fired and moved windows correctly." : $"{problems} problem(s).");

    return problems == 0 ? 0 : 1;
}

if (argv.Contains("--listen"))
{
    // Live hotkey test. This is the real proof that Win+A/S/D can be bound
    // natively without a PowerToys remap in the chain.
    using var engine = new Mullion.Platform.Windows.Hotkeys.HotkeyEngine(new WindowManager());
    engine.Apply(layout, displays);

    engine.Diagnostic += m => Console.WriteLine($"  [diag] {m}");
    engine.Fired += f => Console.WriteLine(
        $"  Win+{surface.FallbackLabelAt(f.Position),-3} -> {f.ZoneName,-28} {f.Result.Outcome} {f.Result.Achieved}");

    engine.Start();

    var health = engine.Health;
    Console.WriteLine($"Hook installed: {health.Installed}");
    Console.WriteLine($"LowLevelHooksTimeout: {health.LowLevelHooksTimeoutMs}ms");
    Console.WriteLine($"Elevated: {health.Elevated}");
    Console.WriteLine();
    Console.WriteLine($"Bound: {BoundKeys()}   (press Win + one of these)");
    Console.WriteLine("Press Ctrl+C to stop. The Start menu should NOT open on a bound key.");
    Console.WriteLine();

    var seconds = 60;
    var forIndex = Array.IndexOf(argv, "--for");
    if (forIndex >= 0 && forIndex + 1 < argv.Length && int.TryParse(argv[forIndex + 1], out var s)) seconds = s;

    var stop = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
    stop.Wait(TimeSpan.FromSeconds(seconds));

    var final = engine.Health;
    Console.WriteLine();
    Console.WriteLine($"Reinstalls: {final.ReinstallCount}   latency p50 {final.LatencyP50Ms:0.###}ms max {final.LatencyMaxMs:0.###}ms");
    return 0;
}

var snapIndex = Array.IndexOf(argv, "--snap");
if (snapIndex >= 0 && snapIndex + 1 < argv.Length)
{
    var wanted = argv[snapIndex + 1].ToUpperInvariant();

    var delay = 3;
    var delayIndex = Array.IndexOf(argv, "--delay");
    if (delayIndex >= 0 && delayIndex + 1 < argv.Length && int.TryParse(argv[delayIndex + 1], out var d)) delay = d;

    var match = surface.Positions()
        .Where(p => layout.At(p) is not null)
        .Cast<GridPos?>()
        .FirstOrDefault(p => surface.FallbackLabelAt(p!.Value).Equals(wanted, StringComparison.OrdinalIgnoreCase));

    if (match is null)
    {
        Console.Error.WriteLine($"No zone bound to '{wanted}'. Bound keys: {BoundKeys()}");
        return 1;
    }

    var zone = layout.At(match.Value)!;
    var target = ProjectZone(zone);

    Console.WriteLine($"Target: {zone.Name}  {target}");
    for (var i = delay; i > 0; i--)
    {
        Console.Write($"\rFocus the window to move... {i} ");
        Thread.Sleep(1000);
    }

    Console.WriteLine();

    var result = new WindowManager().MoveForegroundTo(target);
    Console.WriteLine($"Outcome : {result.Outcome}");
    Console.WriteLine($"Achieved: {result.Achieved}");
    Console.WriteLine($"Attempts: {result.Attempts}");
    if (result.Note is not null) Console.WriteLine($"Note    : {result.Note}");

    var dx = Math.Abs(result.Achieved.Left - target.Left);
    var dy = Math.Abs(result.Achieved.Top - target.Top);
    var dw = Math.Abs(result.Achieved.Width - target.Width);
    var dh = Math.Abs(result.Achieved.Height - target.Height);
    Console.WriteLine($"Delta   : x{dx} y{dy} w{dw} h{dh}");

    return result.Success ? 0 : 1;
}

// ---- default: report ------------------------------------------------------

Console.WriteLine($"Detected {displays.Count} display(s)");
Console.WriteLine();

var conflicts = Mullion.Platform.Windows.Hotkeys.HookConflictDetector.Detect(
    layout.Zones.Select(z => surface.ScanCodeAt(z.Position)),
    Mullion.Core.Hotkeys.ChordModifiers.Win);

if (conflicts.Count > 0)
{
    Console.WriteLine("Hotkey conflicts:");
    foreach (var c in conflicts)
    {
        Console.WriteLine($"  [{c.Severity}] {c.Source}: {c.Summary}");
        foreach (var line in Wrap(c.Advice, 76)) Console.WriteLine($"      {line}");
    }

    Console.WriteLine();
}

Console.WriteLine("Privileges:");
Console.WriteLine($"  elevated  : {Mullion.Platform.Windows.Windows.Elevation.IsCurrentProcessElevated}");
Console.WriteLine($"  integrity : {Mullion.Platform.Windows.Windows.Elevation.CurrentIntegrity}");
Console.WriteLine(Mullion.Platform.Windows.Windows.Elevation.IsCurrentProcessElevated
    ? "  Elevated windows (Task Manager, regedit, admin terminals) are manageable."
    : "  Elevated windows cannot be moved, and hotkeys will not fire while one has focus.");
Console.WriteLine();

if (provider.Diagnostics.Count > 0)
{
    Console.WriteLine("Identity resolution:");
    foreach (var line in provider.Diagnostics) Console.WriteLine($"  {line}");
    Console.WriteLine();
}

foreach (var d in displays)
{
    Console.WriteLine($"  {d.StableKey,-14} {d.FriendlyName}");
    Console.WriteLine($"  {"",-14} gdi      : {d.GdiDeviceName}");
    Console.WriteLine($"  {"",-14} bounds   : {d.Bounds}");
    Console.WriteLine($"  {"",-14} work     : {d.WorkArea}  (taskbar {d.Bounds.Height - d.WorkArea.Height}px)");
    Console.WriteLine($"  {"",-14} dpi      : {d.Dpi} ({d.Scale:P0})");
    Console.WriteLine($"  {"",-14} shape    : {d.Orientation}, elongation {d.Elongation:0.###}, rotation {d.Rotation}");

    var counts = ShapeAnalyzer.ZoneCounts(d.Bounds, d.Dpi, displays.Count > 1);
    Console.WriteLine($"  {"",-14} zones    : {counts.Min}..{counts.Max}, preferred {counts.Preferred}");
    Console.WriteLine($"  {"",-14} primary  : {d.IsPrimary}");
    Console.WriteLine();
}

var columns = DisplayGrid.Columns(displays);
Console.WriteLine($"Grid: {columns.Count} column(s), depths [{string.Join(", ", columns.Select(c => c.Depth))}]");
Console.WriteLine();
Console.WriteLine($"Surface: {surface.Name}");
Console.WriteLine();

for (var row = 0; row < surface.Rows; row++)
{
    var cells = new List<string>();
    for (var col = 0; col < surface.Cols; col++)
    {
        var pos = new GridPos(row, col);
        cells.Add(layout.At(pos) is null ? "  ·  " : $" {surface.FallbackLabelAt(pos),-3} ");
    }

    Console.WriteLine("   " + string.Join("", cells));
}

Console.WriteLine();
Console.WriteLine("Zones:");

foreach (var zone in layout.Zones.OrderBy(z => z.Position.Row).ThenBy(z => z.Position.Col))
{
    Console.WriteLine($"  Win+{surface.FallbackLabelAt(zone.Position),-5} {zone.Kind,-13} {zone.Name}");
    Console.WriteLine($"  {"",-10} {ProjectZone(zone)}");
}

if (layout.Notes.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Notes:");
    foreach (var note in layout.Notes) Console.WriteLine($"  - {note}");
}

Console.WriteLine();
Console.WriteLine("Try:  mullion-probe --snap S --delay 3     then  mullion-probe --undo");
return 0;

Mullion.Core.Geometry.PxRect ProjectZone(Zone zone)
{
    var rects = zone.Parts.Select(p =>
    {
        var display = displays.First(dd => dd.StableKey == p.DisplayKey);
        return p.Area.Project(display.WorkArea);
    });

    return Mullion.Core.Geometry.PxRect.Union(rects);
}

static IEnumerable<string> Wrap(string text, int width)
{
    var line = new System.Text.StringBuilder();

    foreach (var word in text.Split(' '))
    {
        if (line.Length > 0 && line.Length + word.Length + 1 > width)
        {
            yield return line.ToString();
            line.Clear();
        }

        if (line.Length > 0) line.Append(' ');
        line.Append(word);
    }

    if (line.Length > 0) yield return line.ToString();
}

/// <summary>
/// Which Win+letter combinations PowerToys Keyboard Manager already remaps, so
/// the test can say which of its own results are confounded rather than
/// silently claiming credit for PowerToys' interception.
/// </summary>
static HashSet<string> ReadPowerToysWinRemaps()
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "PowerToys", "Keyboard Manager", "default.json");

    if (!File.Exists(path)) return result;

    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("remapShortcuts", out var shortcuts)) return result;
        if (!shortcuts.TryGetProperty("global", out var global)) return result;

        foreach (var entry in global.EnumerateArray())
        {
            if (!entry.TryGetProperty("originalKeys", out var keys)) continue;

            var parts = (keys.GetString() ?? string.Empty).Split(';');
            // 91/92 are the Windows keys; the remainder is the letter.
            if (parts.Length < 2 || (parts[0] != "91" && parts[0] != "92")) continue;
            if (int.TryParse(parts[1], out var vk) && vk is >= 0x41 and <= 0x5A)
                result.Add(((char)vk).ToString());
        }
    }
    catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
    {
        // Not being able to read PowerToys' config is not fatal to the test.
    }

    return result;
}

string BoundKeys() => string.Join(" ", layout.Zones
    .OrderBy(z => z.Position.Row).ThenBy(z => z.Position.Col)
    .Select(z => surface.FallbackLabelAt(z.Position)));

// DllImport, not LibraryImport: the generator that backs LibraryImport emits
// unsafe code, and this project does not allow it for one P/Invoke.
internal static class DragProbe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}