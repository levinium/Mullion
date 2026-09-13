using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
/// The layout cards in the wizard, where the same diagram is drawn at thumbnail
/// size inside a fixed box.
/// <para>
/// A card is a choice between pictures, so the pictures have to be comparable.
/// Neither half of that held. Three of the four options drew the desk at four
/// fifths of the width they were given, because compact mode went on paying for
/// headroom it draws nothing in; the fourth drew it at 5120px inside a 270px
/// card, because its span measure claimed more height than the card had and the
/// scale that came back from a negative budget was 1.
/// </para>
/// <para>
/// Both were visible in a screenshot and neither was obvious in one - the
/// blown-up card looked like a nicely filled thumbnail, because all anyone
/// could see of it was the middle.
/// </para>
/// </summary>
public class WizardPreviewTests
{
    /// <summary>The card's preview frame, as the wizard sizes it.</summary>
    private static readonly Size Card = new(270, 110);

    /// <summary>
    /// How far inside the frame a monitor may stop and still count as reaching
    /// it: the panel's own Gap, which insets every monitor so adjacent ones read
    /// as separate panels rather than one slab, plus a little for rounding.
    /// Not slack for real letterboxing - the bug this catches left a third of
    /// the card empty.
    /// </summary>
    private const double Inset = 12;

    public static TheoryData<string> Arrangements()
    {
        var data = new TheoryData<string>();
        foreach (var t in SimulatedTopologies.All) data.Add(t.Id);
        return data;
    }

    /// <summary>
    /// One preview, measured and arranged exactly as the card does it: a fixed
    /// box the diagram has to fit itself into.
    /// </summary>
    private static MonitorDiagram Render(string topologyId, LayoutResult layout)
    {
        var topology = SimulatedTopologies.Find(topologyId)!;

        var diagram = new MonitorDiagram
        {
            Compact = true,
            DataContext = MonitorDiagramViewModel.Build(
                topology.Displays, layout, defaultModifier: ChordModifiers.Win),
        };

        var window = new Window { Width = Card.Width, Height = Card.Height, Content = diagram };
        window.Show();

        // Several passes, as the detailed-mode tests do: a lane's thickness is
        // whatever its content measured, so nothing settles until something has
        // been measured once.
        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(Card);
            window.Arrange(new Rect(Card));
            Dispatcher.UIThread.RunJobs();
        }

        return diagram;
    }

    /// <summary>Every monitor drawn in the preview, as one rectangle.</summary>
    private static Rect DeskOf(MonitorDiagram diagram)
    {
        var bezels = diagram.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("monitorBezel"))
            .ToList();

        bezels.ShouldNotBeEmpty("the preview has to draw the monitors at all");

        return bezels
            .Select(b => b.Bounds.TransformToAABB(
                b.GetVisualParent()!.TransformToVisual(diagram) ?? Matrix.Identity))
            .Aggregate((a, b) => a.Union(b));
    }

    private static IReadOnlyList<Rect> DesksFor(string topologyId)
    {
        var topology = SimulatedTopologies.Find(topologyId)!;

        return
        [
            .. LayoutCandidates
                .Generate(topology.Displays, KeySurface.LeftHandBlock)
                .Select(c => DeskOf(Render(topologyId, c.Layout)))
        ];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void EveryCardDrawsTheDeskAtTheSameSize(string topologyId)
    {
        // The cards sit side by side and differ only in how the desk is carved
        // up. One drawing it smaller reads as different hardware, which is not
        // what any of these options mean.
        var desks = DesksFor(topologyId);

        var widest = desks.Max(d => d.Width);
        var narrowest = desks.Min(d => d.Width);

        narrowest.ShouldBe(widest, 1.0,
            $"cards drew the desk at widths {string.Join(", ", desks.Select(d => $"{d.Width:F0}"))}");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void EveryCardFillsTheBoxOnWhicheverAxisBinds(string topologyId)
    {
        // What letterboxing means: the picture touches two opposite edges of its
        // frame and the slack is all on the other axis. A thumbnail short of
        // both edges is not a wide desk in a narrow box, it is room taken by
        // something that is not being drawn.
        foreach (var desk in DesksFor(topologyId))
        {
            var fills =
                desk.Width >= Card.Width - Inset ||
                desk.Height >= Card.Height - Inset;

            fills.ShouldBeTrue(
                $"desk drawn {desk.Width:F0}x{desk.Height:F0} in a {Card.Width:F0}x{Card.Height:F0} box");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void NoCardDrawsOutsideItsOwnBox(string topologyId)
    {
        // The failure that looked like success. A desk drawn at 1:1 covers the
        // card edge to edge and reads as a well-filled thumbnail, because the
        // card clips away every part of it that would have given the game away.
        foreach (var desk in DesksFor(topologyId))
        {
            desk.Width.ShouldBeLessThanOrEqualTo(Card.Width + 1);
            desk.Height.ShouldBeLessThanOrEqualTo(Card.Height + 1);
        }
    }
    /// <summary>Every key chip drawn in the preview, in its coordinates.</summary>
    private static IReadOnlyList<Rect> ChipsOf(MonitorDiagram diagram) =>
    [
        .. diagram.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("keyChip"))
            .Where(b => b.Bounds is { Width: > 0, Height: > 0 })
            .Select(b => b.Bounds.TransformToAABB(
                b.GetVisualParent()!.TransformToVisual(diagram) ?? Matrix.Identity))
    ];

    [AvaloniaTheory]
    [MemberData(nameof(Arrangements))]
    public void KeyChipsDoNotSitOnTopOfOneAnother(string topologyId)
    {
        // A column carries three chips - its key, and the halves above and
        // below - and a zone in a card is about fifty pixels tall. At full size
        // the three overlapped so completely that none of the three could be
        // read, which is the one thing the card is choosing between.
        var topology = SimulatedTopologies.Find(topologyId)!;

        foreach (var candidate in LayoutCandidates.Generate(topology.Displays, KeySurface.LeftHandBlock))
        {
            var chips = ChipsOf(Render(topologyId, candidate.Layout));

            for (var i = 0; i < chips.Count; i++)
            {
                for (var j = i + 1; j < chips.Count; j++)
                {
                    var overlap = chips[i].Intersect(chips[j]);

                    // A hairline in one direction is two chips whose borders
                    // meet, which is what a tile too short for three of them
                    // degrades to and is perfectly readable. Sharing a BAND -
                    // several pixels deep in both directions - is two labels
                    // printed over each other, which is the failure.
                    Math.Min(overlap.Width, overlap.Height).ShouldBeLessThan(1.5,
                        $"{candidate.Id}: chips at {chips[i]} and {chips[j]} overlap");
                }
            }
        }
    }
}
