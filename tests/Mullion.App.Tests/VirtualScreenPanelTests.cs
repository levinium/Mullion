using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mullion.App.Controls;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// What the panel does when the annotations beside the desk want more room than
/// there is.
/// <para>
/// It used to answer by drawing the desk at 1:1. The scale came out negative,
/// a guard reading "scale &lt;= 0" as "measured unconstrained" turned that into
/// 1, and a 5120px desk went into a 270px card - which looked, through the
/// card's own clipping, like a well-filled thumbnail.
/// </para>
/// <para>
/// Worth a test of its own rather than only through the diagram: the case is a
/// property of the panel, and the arrangement that happened to trigger it has
/// since stopped putting an annotation there at all.
/// </para>
/// </summary>
public class VirtualScreenPanelTests
{
    private static readonly Size Box = new(270, 110);
    private static readonly Rect Desk = new(0, 0, 5120, 1440);

    /// <summary>
    /// A panel holding one monitor and one annotation of the given height,
    /// measured and arranged into a box far too small for both.
    /// </summary>
    private static (Control Monitor, VirtualScreenPanel Panel) Render(double annotationHeight)
    {
        var monitor = new Border();
        VirtualScreenPanel.SetRect(monitor, Desk);
        VirtualScreenPanel.SetLane(monitor, DiagramLane.Desk);

        var annotation = new Border { Width = 120, Height = annotationHeight };
        VirtualScreenPanel.SetRect(annotation, Desk);
        VirtualScreenPanel.SetLane(annotation, DiagramLane.Bottom);

        var panel = new VirtualScreenPanel
        {
            VirtualBounds = Desk,
            Gap = 10,
            Children = { monitor, annotation },
        };

        var window = new Window { Width = Box.Width, Height = Box.Height, Content = panel };
        window.Show();

        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(Box);
            window.Arrange(new Rect(Box));
            Dispatcher.UIThread.RunJobs();
        }

        return (monitor, panel);
    }

    [AvaloniaFact]
    public void AnAnnotationTallerThanThePanelDoesNotBlowTheDeskUp()
    {
        // The exact shape of the bug: an annotation claiming more height than
        // the whole box existed, so the desk's budget went negative.
        var (monitor, _) = Render(annotationHeight: 200);

        monitor.Bounds.Width.ShouldBeLessThanOrEqualTo(Box.Width + 1,
            "the desk must stay inside the box it was given");
        monitor.Bounds.Height.ShouldBeLessThanOrEqualTo(Box.Height + 1);
    }

    [AvaloniaFact]
    public void TheDeskKeepsHalfTheBoxHoweverGreedyTheAnnotation()
    {
        // Drawing it at nothing is as useless an answer as drawing it at full
        // size. The desk is the subject; the annotation is what makes do.
        var (monitor, _) = Render(annotationHeight: 200);

        monitor.Bounds.Height.ShouldBeGreaterThan(Box.Height / 4,
            "the desk should not be squeezed out by something beside it");
    }

    [AvaloniaFact]
    public void AnAnnotationThatFitsIsStillPaidForInFull()
    {
        // The clamp must not change the ordinary case. With room for both, the
        // desk gets exactly what is left over and no more.
        var (monitor, _) = Render(annotationHeight: 20);

        // 270/5120 is the wider scale, so height binds: 110 - 20 - the lane's
        // own gap, all of it available to a 1440-tall desk.
        var budget = Box.Height - 20 - 4;

        monitor.Bounds.Height.ShouldBeLessThanOrEqualTo(budget + 1,
            "the annotation's room was handed to the desk as well");
    }

    [AvaloniaFact]
    public void AnAnnotationDrawingNothingReservesNothing()
    {
        // Lane thickness is a floor, so an annotation that measures to zero used
        // to go on holding its minimum against a desk it contributed nothing to.
        var (withNothing, _) = Render(annotationHeight: 0);
        var (withSomething, _) = Render(annotationHeight: 40);

        withNothing.Bounds.Height.ShouldBeGreaterThan(withSomething.Bounds.Height,
            "an empty annotation should cost the desk less than a real one");
    }
}
