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
    Console.WriteLine(after == before || after == target.Handle
        ? "Focus stayed with the target window (Start menu did not open)."
        : "Focus moved elsewhere - something else took it.");

    return 0;
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
