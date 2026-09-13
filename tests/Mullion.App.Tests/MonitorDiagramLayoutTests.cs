using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mullion.App.Controls;
using Mullion.App.ViewModels;
using Mullion.Core.Hotkeys;
using Mullion.Core.Layout;
using Mullion.Core.Simulation;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Renders the diagram for every simulated arrangement and checks that things
/// land inside the things that contain them.
/// <para>
/// These exist because the diagram was being checked by looking at screenshots,
/// which catches whatever you thought to look at on the arrangement you happened
/// to capture. Every regression here was found by the user rather than by me:
/// key chips cropped by a narrow monitor, a span measure cut off by its gutter,
/// tier chips that looked centered above and bottom-justified below. Each is a
/// statement about rectangles, so each can be asserted.
/// </para>
/// </summary>
public class MonitorDiagramLayoutTests
{
    /// <summary>Wide enough for the desk, short enough to force real scaling.</summary>
    private static readonly Size Canvas = new(1040, 360);

    /// <summary>
    /// The longest modifier offered. Every size assertion runs with it, because
    /// the failures all appeared when chips grew from "A" to "Win+A" and would
    /// reappear at "Ctrl+Shift+A".
    /// </summary>
    private const string LongestPrefix = "Ctrl+Shift+";

    /// <summary>The same chord as an enum, since the diagram derives the prefix from it.</summary>
    private const ChordModifiers LongestModifier = ChordModifiers.Control | ChordModifiers.Shift;

    /// <summary>
    /// Slack for arrange rounding and the bezel's own 2px border - not for real
    /// overflow. The bug this catches was a chip 105px wider than its monitor.
    /// </summary>
    private const double Slack = 2.5;

    public static TheoryData<string> Arrangements()
    {
        var data = new TheoryData<string>();
        foreach (var t in SimulatedTopologies.All) data.Add(t.Id);
        return data;
    }

    private static MonitorDiagram Render(string topologyId, ChordModifiers modifier, Size? canvas = null)
    {
        var size = canvas ?? Canvas;

        var topology = SimulatedTopologies.Find(topologyId)!;
        var layout = LayoutBuilder.Build(topology.Displays, KeySurface.LeftHandBlock);

        var diagram = new MonitorDiagram
        {
            DataContext = MonitorDiagramViewModel.Build(
                topology.Displays, layout, defaultModifier: modifier),
        };

        var window = new Window { Width = size.Width, Height = size.Height, Content = diagram };
        window.Show();

        // Several passes, pumping the dispatcher between them. One is not
        // enough: the panel reserves gutters from what its children measure, and
        // constraints bound to an arranged size cannot be known until something
        // has been arranged. Without RunJobs those bindings never deliver at
        // all, so the test would measure a half-built layout the app never shows.
        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(size);
            window.Arrange(new Rect(size));
            Dispatcher.UIThread.RunJobs();
        }

        return diagram;
    }

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>();

    /// <summary>Bounds of a visual in the diagram's own coordinates.</summary>
    private static Rect BoundsIn(Visual visual, Visual root) =>
        visual.Bounds.TransformToAABB(visual.GetVisualParent()!.TransformToVisual(root) ?? Matrix.Identity);

    // ---- The failures that reached the user, as assertions ------------------

    /// <summary>
    /// A key chip must fit inside the monitor it belongs to. The bezel clips, so
    /// anything wider is cut off mid-chord - which is how "Win+Q" became "Win+"
    /// on a rotated 32:9.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void KeyChipsFitInsideTheirMonitor(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);

        foreach (var bezel in Descendants<Border>(diagram).Where(b => b.Classes.Contains("monitorBezel")))
        {
            var frame = BoundsIn(bezel, diagram);

            foreach (var chip in Descendants<Border>(bezel).Where(b => b.Classes.Contains("keyChip")))
            {
                if (!chip.IsVisible || chip.Bounds.Width <= 0) continue;

                var box = BoundsIn(chip, diagram);

                box.Left.ShouldBeGreaterThanOrEqualTo(frame.Left - Slack,
                    $"{topologyId}: a key chip starts left of its monitor");
                box.Right.ShouldBeLessThanOrEqualTo(frame.Right + Slack,
                    $"{topologyId}: a key chip runs past the right of its monitor");
            }
        }
    }

    /// <summary>
    /// The two subzone chips must sit the same distance from the middle of their
    /// tile. Pinned to its edges instead, the pair reads as one centered and one
    /// edge-justified.
    /// <para>
    /// Measured along whichever axis actually separates them, since subzones are
    /// no longer always stacked: a side-by-side pair is symmetric left and right
    /// of the middle and shares one line vertically, which under the original
    /// vertical-only form of this assertion looked maximally lopsided.
    /// </para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void TierChipsAreSymmetricAboutTheirTile(string topologyId)
    {
        var diagram = Render(topologyId, ChordModifiers.Win);

        foreach (var tile in Descendants<Button>(diagram).Where(b => b.Classes.Contains("zoneTileButton")))
        {
            // The chip TOGETHER WITH its size label, which is what the eye reads
            // as one subzone's marker. Measuring the bare chip works only while
            // the pair is stacked and both carry the same offset from their
            // label; side by side that offset lands on opposite sides of the
            // middle and shows up as an asymmetry that is not there.
            var chips = Descendants<Border>(tile)
                .Where(b => b.Classes.Contains("tierChip") && b.IsVisible && b.Bounds.Height > 0)
                .Select(b => (Visual)(b.GetVisualAncestors().OfType<Viewbox>().FirstOrDefault() ?? (Visual)b))
                .Select(v => BoundsIn(v, diagram))
                .ToList();

            if (chips.Count != 2) continue;

            var frame = BoundsIn(tile, diagram);

            // Whichever axis the pair is spread along is the one symmetry means
            // anything on; the other holds them on a common line.
            var spreadY = Math.Abs(chips[0].Center.Y - chips[1].Center.Y);
            var spreadX = Math.Abs(chips[0].Center.X - chips[1].Center.X);
            var stacked = spreadY >= spreadX;

            var ordered = stacked
                ? chips.OrderBy(r => r.Center.Y).ToList()
                : chips.OrderBy(r => r.Center.X).ToList();

            var (mid, first, second) = stacked
                ? (frame.Center.Y, ordered[0].Center.Y, ordered[1].Center.Y)
                : (frame.Center.X, ordered[0].Center.X, ordered[1].Center.X);

            var before = mid - first;
            var after = second - mid;

            Math.Abs(before - after).ShouldBeLessThan(2,
                $"{topologyId}: subzone chips sit {before:F0}px and {after:F0}px either side of the middle " +
                $"({(stacked ? "stacked" : "side by side")})");
        }
    }

    /// <summary>
    /// Nothing inside a tile may sit on top of anything else in it. A short
    /// display is the case that breaks: the tier chips sit at the quarter marks
    /// while the zone's own chip, name and size sit in the middle, and on a tile
    /// too short to hold both they land on each other.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void NothingInATileOverlapsAnythingElse(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);

        foreach (var tile in Descendants<Button>(diagram).Where(b => b.Classes.Contains("zoneTileButton")))
        {
            // Chips and their labels: the things that carry text, and so the
            // things whose overlapping is unreadable rather than merely untidy.
            var parts = Descendants<Border>(tile)
                .Where(b => b.Classes.Contains("keyChip") && b.IsVisible && b.Bounds.Width > 0)
                .Select(b => (Kind: "chip", Box: BoundsIn(b, diagram)))
                .Concat(Descendants<TextBlock>(tile)
                    .Where(t => t.IsVisible && t.Bounds.Width > 0 && !string.IsNullOrEmpty(t.Text))
                    .Where(t => t.GetVisualAncestors().OfType<Border>().All(b => !b.Classes.Contains("keyChip")))
                    .Select(t => (Kind: $"text '{t.Text}'", Box: BoundsIn(t, diagram))))
                .ToList();

            for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                parts[i].Box.Intersects(parts[j].Box).ShouldBeFalse(
                    $"{topologyId}: {parts[i].Kind} {Say(parts[i].Box)} " +
                    $"overlaps {parts[j].Kind} {Say(parts[j].Box)} " +
                    $"in tile {Say(BoundsIn(tile, diagram))}");
            }
        }
    }

    /// <summary>Coordinates, for a failure message that can be acted on.</summary>
    private static string Say(Rect r) =>
        $"[{r.X:F0},{r.Y:F0} {r.Width:F0}x{r.Height:F0}]";

    /// <summary>
    /// No run of text may be arranged narrower than it needs.
    /// <para>
    /// A TextBlock given less width than its text clips it rather than
    /// overflowing, and the part that disappears is the end - which on a chord
    /// is the "+" or the key itself. Nothing about the layout looks wrong when
    /// this happens; the text is simply, silently, not all there. Comparing
    /// arranged width against desired width is the only way to see it.
    /// </para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void NoTextIsClipped(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);

        foreach (var text in Descendants<TextBlock>(diagram))
        {
            if (!text.IsVisible || string.IsNullOrEmpty(text.Text)) continue;
            if (text.TextWrapping != TextWrapping.NoWrap) continue;

            // Half a pixel of slack for rounding; anything more is a lost glyph.
            text.Bounds.Width.ShouldBeGreaterThanOrEqualTo(text.DesiredSize.Width - 0.5,
                $"{topologyId}: '{text.Text}' is cut off - " +
                $"{text.Bounds.Width:F1}px given, {text.DesiredSize.Width:F1}px needed");
        }
    }

    /// <summary>
    /// Room a label needs between itself and the zone border. Touching it, the
    /// label reads as clipped whether or not a pixel is actually lost.
    /// </summary>
    private const double LabelClearance = 4;

    /// <summary>
    /// A zone's name and size must sit inside their tile.
    /// <para>
    /// They are dropped on a tile too short to hold them, but "too short" was a
    /// constant measured against a chip whose chord fits on one line. A chord
    /// that wraps makes the chip two or three times taller, and the name it
    /// pushes down lands on the zone's own border.
    /// </para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void ZoneLabelsStayInsideTheirTile(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);

        foreach (var tile in Descendants<Button>(diagram).Where(b => b.Classes.Contains("zoneTileButton")))
        {
            var frame = BoundsIn(tile, diagram).Deflate(LabelClearance);

            var labels = Descendants<TextBlock>(tile)
                .Where(t => t.IsVisible && t.Bounds.Height > 0)
                .Where(t => t.Classes.Contains("zoneName") || t.Classes.Contains("zoneSize"));

            foreach (var label in labels)
            {
                var box = BoundsIn(label, diagram);

                box.Top.ShouldBeGreaterThanOrEqualTo(frame.Top,
                    $"{topologyId}: '{label.Text}' runs off the top of its zone");
                box.Bottom.ShouldBeLessThanOrEqualTo(frame.Bottom,
                    $"{topologyId}: '{label.Text}' sits on the bottom of its zone " +
                    $"[tile {frame.Height:F0}px, label bottom {box.Bottom:F0} vs {frame.Bottom:F0}]");
            }
        }
    }

    /// <summary>
    /// A chip must sit inside its tile with room to spare, not flush against the
    /// zone's own border - which reads as the chip having burst out of it.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void ChipsKeepClearOfTheirTileEdges(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);

        foreach (var tile in Descendants<Button>(diagram).Where(b => b.Classes.Contains("zoneTileButton")))
        {
            var frame = BoundsIn(tile, diagram).Deflate(2);

            foreach (var chip in Descendants<Border>(tile).Where(b => b.Classes.Contains("keyChip")))
            {
                if (!chip.IsVisible || chip.Bounds.Width <= 0) continue;

                var box = BoundsIn(chip, diagram);

                box.Left.ShouldBeGreaterThanOrEqualTo(frame.Left,
                    $"{topologyId}: a chip is flush with the left of its zone");
                box.Right.ShouldBeLessThanOrEqualTo(frame.Right,
                    $"{topologyId}: a chip is flush with the right of its zone " +
                    $"[{string.Join(' ', chip.Classes)}] box={box} tile={frame}");
            }
        }
    }

    /// <summary>
    /// A span measure's chip must fit the gutter opened for it. The gutter is
    /// sized from what the measure reports, so a chip wider than that is a
    /// measurement that did not reach the panel.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void SpanMeasuresFitTheirGutter(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);

        foreach (var measure in Descendants<Button>(diagram).Where(b => b.Classes.Contains("spanMeasure")))
        {
            var frame = BoundsIn(measure, diagram);

            foreach (var chip in Descendants<Border>(measure).Where(b => b.Classes.Contains("keyChip")))
            {
                if (!chip.IsVisible || chip.Bounds.Width <= 0) continue;

                var box = BoundsIn(chip, diagram);

                box.Left.ShouldBeGreaterThanOrEqualTo(frame.Left - Slack,
                    $"{topologyId}: a span measure's chip is cut off on the left");
                box.Right.ShouldBeLessThanOrEqualTo(frame.Right + Slack,
                    $"{topologyId}: a span measure's chip is cut off on the right");
            }
        }
    }

    /// <summary>Nothing may be pushed outside the diagram itself.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void EverythingStaysInsideTheDiagram(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);
        var frame = new Rect(diagram.Bounds.Size);

        foreach (var bezel in Descendants<Border>(diagram).Where(b => b.Classes.Contains("monitorBezel")))
        {
            var box = BoundsIn(bezel, diagram);

            box.Left.ShouldBeGreaterThanOrEqualTo(-1, $"{topologyId}: a monitor is off the left edge");
            box.Right.ShouldBeLessThanOrEqualTo(frame.Right + Slack, $"{topologyId}: a monitor is off the right edge");
            box.Top.ShouldBeGreaterThanOrEqualTo(-1, $"{topologyId}: a monitor is off the top edge");
        }
    }

    /// <summary>
    /// Monitor name tabs must not reach a neighbor's. They overflow their own
    /// display on purpose, which is exactly why they need checking.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void MonitorTabsDoNotOverlapEachOther(string topologyId)
    {
        var diagram = Render(topologyId, ChordModifiers.Win);

        var tabs = Descendants<Border>(diagram)
            .Where(b => b.Classes.Contains("monitorLabel") && b.IsVisible && b.Bounds.Width > 0)
            .Select(b => BoundsIn(b, diagram))
            .ToList();

        for (var i = 0; i < tabs.Count; i++)
        for (var j = i + 1; j < tabs.Count; j++)
        {
            tabs[i].Intersects(tabs[j]).ShouldBeFalse($"{topologyId}: two monitor tabs overlap");
        }
    }

    /// <summary>
    /// The chord is what the diagram is for. A chip may shrink to fit, but it
    /// must never drop the modifier and show a key that is not the hotkey.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void EveryChipShowsTheWholeChord(string topologyId)
    {
        var diagram = Render(topologyId, LongestModifier);

        foreach (var chip in Descendants<Border>(diagram).Where(b => b.Classes.Contains("keyChip")))
        {
            if (!chip.IsVisible) continue;

            var texts = Descendants<TextBlock>(chip).Where(t => t.IsVisible).ToList();
            if (texts.Count == 0) continue;

            // Concatenated, because the chord is drawn as separate runs so a
            // panel can wrap between them. What must never happen is a chip
            // showing a bare key: that is not the hotkey it names.
            var chord = string.Concat(texts.Select(t => t.Text));

            chord.StartsWith(LongestPrefix, StringComparison.Ordinal).ShouldBeTrue(
                $"{topologyId}: a chip shows '{chord}' rather than the whole chord");
        }
    }

    /// <summary>
    /// A monitor's name tab must not sit on top of a key chip.
    /// <para>
    /// The tab is drawn inside the top-left corner for any display with another
    /// directly above it, because there is no headroom to put it in. On a short
    /// display that corner is also where the zone's own chip ends up, and the
    /// two were printing over each other - two stacked monitors flanked by
    /// verticals put "Win+X" across the words "Center lower".
    /// </para>
    /// <para>
    /// Stated as rectangles rather than as a rule about which arrangements are
    /// affected, because the next one that collides will not be on the list.
    /// </para>
    /// </summary>
    /// <summary>
    /// Diagram sizes to check the tab against, because the collision depends on
    /// one. The default harness canvas is wide and short; the window the app
    /// actually opens at is neither, and that is where this was first seen.
    /// </summary>
    public static TheoryData<string, double, double> ArrangementsAndSizes()
    {
        var data = new TheoryData<string, double, double>();

        // Swept rather than sampled. The collision appears at some sizes and not
        // others, so three convenient numbers would only prove that those three
        // were clear - and the threshold that clears them would be fitted to the
        // sample rather than to the problem.
        foreach (var t in SimulatedTopologies.All)
        {
            for (var width = 560d; width <= 1100; width += 60)
            {
                for (var height = 220d; height <= 400; height += 30)
                {
                    data.Add(t.Id, width, height);
                }
            }
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(ArrangementsAndSizes))]
    public void AMonitorsNameTabDoesNotCoverItsKeyChips(string topologyId, double width, double height)
    {
        var diagram = Render(topologyId, LongestModifier, new Size(width, height));

        var labels = Descendants<Border>(diagram)
            .Where(b => b.Classes.Contains("monitorLabel") && b.IsVisible && b.Bounds.Width > 0);

        foreach (var label in labels)
        {
            // Up to the display, not the bezel. The tab's canvas is a SIBLING of
            // the bezel with a ZIndex that puts it over the zones, so scoping
            // this to the bezel finds no tabs at all and quietly checks nothing.
            var display = label.GetVisualAncestors()
                .OfType<Visual>()
                .FirstOrDefault(a => Descendants<Border>(a).Any(b => b.Classes.Contains("monitorBezel")));

            if (display is null) continue;

            var tab = BoundsIn(label, diagram);

            foreach (var chip in Descendants<Border>(display).Where(b => b.Classes.Contains("keyChip")))
            {
                if (!chip.IsVisible || chip.Bounds.Width <= 0) continue;

                var box = BoundsIn(chip, diagram);
                var over = tab.Intersect(box);

                // A shared edge is not a collision; ink over ink is.
                (over.Width * over.Height).ShouldBeLessThanOrEqualTo(1,
                    $"{topologyId} at {width}x{height}: the name tab at {tab} covers a key chip at {box}");
            }
        }
    }

    /// <summary>
    /// The tab that yields must not yield always.
    /// <para>
    /// The fix for the collision above is that a display too short to carry its
    /// name tab inside it does without one. That has an obvious degenerate
    /// solution - never draw the tab - which would pass the collision test
    /// perfectly and lose the labelling on every stacked desk. This is the
    /// assertion that keeps the first test honest.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void AStackedDisplayWithRoomStillGetsItsName()
    {
        // Two monitors stacked: the lower one has no headroom above it, so its
        // name goes inside - and at this size there is plenty of room for it.
        var diagram = Render("two-stacked", LongestModifier, new Size(1040, 560));

        var inside = Descendants<Border>(diagram)
            .Where(b => b.Classes.Contains("monitorLabel") && b.IsVisible && b.Bounds.Width > 0)
            .ToList();

        inside.Count.ShouldBeGreaterThanOrEqualTo(2,
            "both displays should be named - one tab above, one inside");

        var named = inside
            .SelectMany(b => Descendants<TextBlock>(b))
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        // "Bottom" is the lower of the two in this arrangement, and the one
        // with a display directly above it - so it is the one whose tab has to
        // sit inside the monitor rather than above it.
        named.ShouldContain(t => t!.Contains("Bottom", StringComparison.OrdinalIgnoreCase),
            "the lower display is the one whose tab sits inside it");
    }
}
