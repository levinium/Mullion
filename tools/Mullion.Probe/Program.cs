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

string BoundKeys() => string.Join(" ", layout.Zones
    .OrderBy(z => z.Position.Row).ThenBy(z => z.Position.Col)
    .Select(z => surface.FallbackLabelAt(z.Position)));
