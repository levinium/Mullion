using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mullion.App.Controls;
using Mullion.App.ViewModels;
using Mullion.Core.Layout;
using Mullion.Core.Simulation;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// The seam handles, checked at the view model rather than through the visual
/// tree: what matters is that a drag produces the weights that get saved, and
/// that is arithmetic on rectangles.
/// </summary>
public class SplitDraggingTests
{
    private static MonitorDiagramViewModel Build(
        string topologyId, out List<(string Slot, IReadOnlyList<double> Weights)> saved)
    {
        var topology = SimulatedTopologies.Find(topologyId)!;
        var layout = LayoutBuilder.Build(topology.Displays, Core.Hotkeys.KeySurface.LeftHandBlock);

        var captured = new List<(string, IReadOnlyList<double>)>();
        saved = captured;

        return MonitorDiagramViewModel.Build(
            topology.Displays, layout,
            onSplitChanged: (slot, weights) => captured.Add((slot, weights)));
    }

    [Fact]
    public void APlainDiagramHasNoSeams()
    {
        // Built without a commit callback - the wizard and the main window draw
        // the same picture but are not places to reshape it.
        var topology = SimulatedTopologies.Find("single-32-9")!;
        var layout = LayoutBuilder.Build(topology.Displays, Core.Hotkeys.KeySurface.LeftHandBlock);

        var diagram = MonitorDiagramViewModel.Build(topology.Displays, layout);

        diagram.Displays.ShouldAllBe(d => !d.HasHandles);
    }

    [Fact]
    public void ThereIsOneSeamFewerThanThereAreZones()
    {
        var diagram = Build("single-32-9", out _);

        var display = diagram.Displays.Single();

        display.Cells.Count.ShouldBe(3);
        display.Handles.Count.ShouldBe(2);
    }

    [Fact]
    public void AnUndividedDisplayHasNoSeams()
    {
        // Two 16:9s side by side take one zone each. A display that is not split
        // has no interior boundary, and its outer edges are not draggable.
        var diagram = Build("two-across", out _);

        diagram.Displays.ShouldAllBe(d => d.Handles.Count == 0);
    }

    [Fact]
    public void SeamsSitWhereTheZonesMeet()
    {
        var diagram = Build("single-32-9", out _);

        var display = diagram.Displays.Single();
        var cells = display.Cells.OrderBy(c => c.Area.X).ToList();

        display.Handles[0].Position.ShouldBe(cells[0].Area.Right, 1e-6);
        display.Handles[1].Position.ShouldBe(cells[1].Area.Right, 1e-6);
    }

    [Fact]
    public void DraggingASeamMovesTheZonesEitherSideAndNoOthers()
    {
        var diagram = Build("single-32-9", out _);

        var display = diagram.Displays.Single();
        var third = display.Cells.OrderBy(c => c.Area.X).ToList()[2];
        var untouchedWidth = third.Area.Width;

        display.Handles[0].Dragged!(0, 0.2);

        var cells = display.Cells.OrderBy(c => c.Area.X).ToList();

        cells[0].Area.Right.ShouldBe(0.2, 1e-6);
        cells[1].Area.X.ShouldBe(0.2, 1e-6);

        // The far zone is not the user's business here and must not move.
        cells[2].Area.Width.ShouldBe(untouchedWidth, 1e-6);
    }

    [Fact]
    public void ZonesStayFlushWhileDragging()
    {
        // The whole point of the layout: no seam, no overlap. A drag that opened
        // a gap would be saved as one.
        var diagram = Build("single-32-9", out _);

        var display = diagram.Displays.Single();

        foreach (var target in new[] { 0.15, 0.3, 0.45, 0.6 })
        {
            display.Handles[0].Dragged!(0, target);

            var cells = display.Cells.OrderBy(c => c.Area.X).ToList();

            for (var i = 1; i < cells.Count; i++)
                cells[i].Area.X.ShouldBe(cells[i - 1].Area.Right, 1e-6);
        }
    }

    [Fact]
    public void ASeamStopsAtThePixelFloorTheLayoutEngineUses()
    {
        // Dragging is not a way around the rule that a zone too narrow to hold
        // a window is not worth having. The floor is the same 560 logical px
        // the generator refuses to go below, so a hand-made zone is no smaller
        // than a derived one.
        var diagram = Build("single-32-9", out _);

        var display = diagram.Displays.Single();

        display.Handles[0].Dragged!(0, -5);

        var narrowest = display.Cells.Min(c => c.Area.Width) * display.Bounds.Width;

        narrowest.ShouldBe(ShapeTuning.Default.MinZoneLogicalPx, 1.0);
    }

    [Fact]
    public void ReleasingSavesAgainstTheGeometrySlotNotTheMonitor()
    {
        // Slot, so plugging in a different panel of the same shape in the same
        // place keeps the split the user made.
        var diagram = Build("single-32-9", out var saved);

        var display = diagram.Displays.Single();

        display.Handles[0].Dragged!(0, 0.3);
        display.Handles[0].Released!();

        var (slot, weights) = saved.ShouldHaveSingleItem();

        slot.ShouldBe(display.Slot);
        slot.ShouldNotBe(display.Key);
        weights.Sum().ShouldBe(1.0, 1e-6);
        weights[0].ShouldBe(0.3, 1e-6);
    }

    [Fact]
    public void AStackedDisplaysSeamsRunAcrossIt()
    {
        // A rotated 32:9 splits into stacked zones, so its seams are horizontal
        // lines and a drag moves them up and down.
        var diagram = Build("rotated-32-9", out _);

        var display = diagram.Displays.Single();

        display.SeamOrientation.ShouldBe(Avalonia.Layout.Orientation.Vertical);
        display.Handles.Count.ShouldBe(2);

        var cells = display.Cells.OrderBy(c => c.Area.Y).ToList();
        display.Handles[0].Position.ShouldBe(cells[0].Area.Bottom, 1e-6);

        display.Handles[0].Dragged!(0, 0.2);
        display.Cells.OrderBy(c => c.Area.Y).First().Area.Bottom.ShouldBe(0.2, 1e-6);
    }

    [Fact]
    public void ASeamStopsShortOfTheTaskbar()
    {
        // Zones sit in the work area, so a seam drawn the full height of the
        // display would overhang them into the taskbar strip.
        var diagram = Build("single-32-9", out _);

        var display = diagram.Displays.Single();
        var cells = display.Cells;

        display.Handles[0].From.ShouldBe(cells.Min(c => c.Area.Y), 1e-6);
        display.Handles[0].To.ShouldBe(cells.Max(c => c.Area.Bottom), 1e-6);
        display.Handles[0].To.ShouldBeLessThan(1.0);
    }
}

/// <summary>
/// The seams as actually rendered. The view model can be perfectly correct and
/// still produce nothing grabbable: an overlay that measures to nothing, or one
/// the zone tiles cover, looks identical in the tree and inert under the cursor.
/// </summary>
public class SeamRenderingTests
{
    private static readonly Size Canvas = new(1040, 360);

    private static MonitorDiagram Render(string topologyId)
    {
        var topology = SimulatedTopologies.Find(topologyId)!;
        var layout = LayoutBuilder.Build(topology.Displays, Core.Hotkeys.KeySurface.LeftHandBlock);

        var diagram = new MonitorDiagram
        {
            DataContext = MonitorDiagramViewModel.Build(
                topology.Displays, layout,
                onSplitChanged: (_, _) => { }),
        };

        var window = new Window { Width = Canvas.Width, Height = Canvas.Height, Content = diagram };
        window.Show();

        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(Canvas);
            window.Arrange(new Rect(Canvas));
            Dispatcher.UIThread.RunJobs();
        }

        return diagram;
    }

    [AvaloniaFact]
    public void TheSeamsAreRenderedAndGrabbable()
    {
        var diagram = Render("single-32-9");

        var handles = diagram.GetVisualDescendants().OfType<SplitHandle>().ToList();

        handles.Count.ShouldBe(2, "the 25/50/25 split has two interior seams");

        foreach (var handle in handles)
        {
            handle.IsVisible.ShouldBeTrue();
            handle.Bounds.Width.ShouldBeGreaterThan(6, "too narrow to hit with a cursor");
            handle.Bounds.Height.ShouldBeGreaterThan(20, "too short to hit with a cursor");
        }
    }

    [AvaloniaFact]
    public void ASeamSitsOnTheLineBetweenTwoZones()
    {
        var diagram = Render("single-32-9");

        var tiles = diagram.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("zoneTileButton"))
            .Select(b => b.Bounds.TransformToAABB(b.GetVisualParent()!.TransformToVisual(diagram)!.Value))
            .OrderBy(r => r.X)
            .ToList();

        var handles = diagram.GetVisualDescendants().OfType<SplitHandle>()
            .Select(h => h.Bounds.TransformToAABB(h.GetVisualParent()!.TransformToVisual(diagram)!.Value))
            .OrderBy(r => r.X)
            .ToList();

        handles.Count.ShouldBe(2);

        // Within a few pixels of where the first two tiles meet.
        var seam = (tiles[0].Right + tiles[1].Left) / 2;
        handles[0].Center.X.ShouldBe(seam, 6);
    }

    [AvaloniaFact]
    public void DraggingASeamWithThePointerReshapesTheSplitAndSavesIt()
    {
        // The path nothing else covers: a real press, a real move, a real
        // release, through the same hit-testing and pointer capture the cursor
        // goes through. Every other test here stops one step short of it - the
        // view model is called directly, or the control is only measured.
        var topology = SimulatedTopologies.Find("single-32-9")!;
        var layout = LayoutBuilder.Build(topology.Displays, Core.Hotkeys.KeySurface.LeftHandBlock);

        var saved = new List<(string Slot, IReadOnlyList<double> Weights)>();

        var diagram = new MonitorDiagram
        {
            DataContext = MonitorDiagramViewModel.Build(
                topology.Displays, layout,
                onSplitChanged: (slot, weights) => saved.Add((slot, weights))),
        };

        var window = new Window { Width = Canvas.Width, Height = Canvas.Height, Content = diagram };
        window.Show();

        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(Canvas);
            window.Arrange(new Rect(Canvas));
            Dispatcher.UIThread.RunJobs();
        }

        var handle = diagram.GetVisualDescendants().OfType<SplitHandle>()
            .OrderBy(h => h.Bounds.X)
            .First();

        var box = handle.Bounds.TransformToAABB(
            handle.GetVisualParent()!.TransformToVisual(window)!.Value);

        var grab = box.Center;
        var drop = grab.WithX(grab.X - 40);

        window.MouseMove(grab);
        window.MouseDown(grab, MouseButton.Left);
        window.MouseMove(drop);
        Dispatcher.UIThread.RunJobs();

        var display = ((MonitorDiagramViewModel)diagram.DataContext!).Displays.Single();
        var first = display.Cells.OrderBy(c => c.Area.X).First();

        first.Area.Width.ShouldBeLessThan(0.25,
            "the left zone should have narrowed as the seam was dragged left");

        window.MouseUp(drop, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var (_, weights) = saved.ShouldHaveSingleItem();
        weights[0].ShouldBe(first.Area.Width / (1 - 0), 1e-6);
        weights.Sum().ShouldBe(1.0, 1e-6);
    }

    [AvaloniaFact]
    public void ASeamIsWiredToTheViewModel()
    {
        // The gap the other tests leave: they call the delegate on the view
        // model directly, and they check that a control was drawn. Neither says
        // the template actually connected the two, so a seam can render
        // perfectly, sit exactly where it should, and do nothing when dragged.
        var diagram = Render("single-32-9");

        var handles = diagram.GetVisualDescendants().OfType<SplitHandle>()
            .OrderBy(h => h.Bounds.X)
            .ToList();

        handles.Count.ShouldBe(2);

        for (var i = 0; i < handles.Count; i++)
        {
            handles[i].Dragged.ShouldNotBeNull($"seam {i} reports nowhere when dragged");
            handles[i].Released.ShouldNotBeNull($"seam {i} saves nothing when released");
            handles[i].Index.ShouldBe(i, "a seam that reports the wrong index moves the wrong zones");
        }
    }

    [AvaloniaFact]
    public void NothingCoversASeam()
    {
        // A seam under the zone tiles is a control the cursor can never reach,
        // which is as useless as not drawing it at all. Asserted by pressing on
        // it rather than by inspecting the tree: what matters is where a real
        // click lands, and the zone tiles are buttons that will happily take it.
        var topology = SimulatedTopologies.Find("single-32-9")!;
        var layout = LayoutBuilder.Build(topology.Displays, Core.Hotkeys.KeySurface.LeftHandBlock);

        var diagram = new MonitorDiagram
        {
            DataContext = MonitorDiagramViewModel.Build(
                topology.Displays, layout,
                onZoneActivated: _ => { },
                onSplitChanged: (_, _) => { }),
        };

        var window = new Window { Width = Canvas.Width, Height = Canvas.Height, Content = diagram };
        window.Show();

        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(Canvas);
            window.Arrange(new Rect(Canvas));
            Dispatcher.UIThread.RunJobs();
        }

        foreach (var handle in diagram.GetVisualDescendants().OfType<SplitHandle>())
        {
            var middle = handle.Bounds
                .TransformToAABB(handle.GetVisualParent()!.TransformToVisual(window)!.Value)
                .Center;

            window.MouseMove(middle);
            window.MouseDown(middle, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            handle.IsDragging.ShouldBeTrue(
                $"a press at the seam's own middle ({middle}) did not reach it");

            window.MouseUp(middle, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
