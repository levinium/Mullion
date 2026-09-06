using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Model;
using Mullion.Platform.Windows.Displays;

// Diagnostic harness: enumerate the real displays and print the layout the
// engine derives from them. This is the first reality check on hardware -
// everything up to here was verified only against synthetic fixtures.

WindowsDisplayProvider.EnsurePerMonitorDpiAwareness();

var provider = new WindowsDisplayProvider();
var displays = provider.GetDisplays();

Console.WriteLine($"Detected {displays.Count} display(s)");
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

var surface = KeySurface.LeftHandBlock;
var layout = LayoutBuilder.Build(displays, surface);

Console.WriteLine($"Surface: {surface.Name}");
Console.WriteLine();

for (var row = 0; row < surface.Rows; row++)
{
    var cells = new List<string>();
    for (var col = 0; col < surface.Cols; col++)
    {
        var pos = new GridPos(row, col);
        var zone = layout.At(pos);
        cells.Add(zone is null
            ? "  ·  "
            : $" {surface.FallbackLabelAt(pos),-3} ");
    }

    Console.WriteLine("   " + string.Join("", cells));
}

Console.WriteLine();
Console.WriteLine("Zones:");

foreach (var zone in layout.Zones.OrderBy(z => z.Position.Row).ThenBy(z => z.Position.Col))
{
    var label = surface.FallbackLabelAt(zone.Position);
    var rects = zone.Parts.Select(p =>
    {
        var display = displays.First(d => d.StableKey == p.DisplayKey);
        return p.Area.Project(display.WorkArea).ToString();
    });

    Console.WriteLine($"  Win+{label,-5} {zone.Kind,-13} {zone.Name}");
    Console.WriteLine($"  {"",-10} {string.Join(" + ", rects)}");
}

if (layout.Notes.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Notes:");
    foreach (var note in layout.Notes) Console.WriteLine($"  - {note}");
}
