using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
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
/// What the diagram does under a pointer, depending on whether it is a control
/// or a picture.
/// <para>
/// A diagram outside edit mode has to be completely inert: it is somewhere
/// people glance at, and anything that lights up or shifts under the cursor
/// promises a click that does nothing. The view model already said so - its
/// cells report IsInteractive false - and the styles disagreed anyway, which is
/// exactly why this is asserted against rendered controls rather than against
/// the view model. The middle band's hover was reached by a selector that never
/// mentioned interactivity, so every assertion about the view model passed while
/// the outline still thickened and nudged the chip beside it.
/// </para>
/// </summary>
public class DiagramHoverTests
{
    /// <summary>Wide enough for the desk, short enough to force real scaling.</summary>
    private static readonly Size Canvas = new(1040, 360);

    /// <summary>
    /// Whether a zone can be clicked is decided by nothing more than whether
    /// anything was handed in to receive the click.
    /// </summary>
    private static Window Render(bool interactive)
    {
        var topology = SimulatedTopologies.Find("single-32-9")!;
        var layout = LayoutBuilder.Build(topology.Displays, KeySurface.LeftHandBlock);

        var diagram = new MonitorDiagram
        {
            DataContext = MonitorDiagramViewModel.Build(
                topology.Displays,
                layout,
                onZoneActivated: interactive ? _ => { } : null),
        };

        var window = new Window { Width = Canvas.Width, Height = Canvas.Height, Content = diagram };
        window.Show();

        Settle(window);
        return window;
    }

    /// <summary>
    /// Several passes, pumping the dispatcher between them: constraints bound to
    /// an arranged size cannot be known until something has been arranged.
    /// </summary>
    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(Canvas);
            window.Arrange(new Rect(Canvas));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static IReadOnlyList<Border> Of(Visual root, string @class) =>
        [.. root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains(@class))];

    /// <summary>
    /// The pseudo-class directly rather than a synthetic mouse move. What is
    /// being tested is which styles answer to a hover, not whether the band can
    /// be hit - it is transparent-filled precisely so it always can, since
    /// pointing at a zone still has to be able to describe it.
    /// </summary>
    private static void Hover(Border band, bool hovering)
    {
        var pseudo = (IPseudoClasses)band.Classes;

        if (hovering) pseudo.Add(":pointerover");
        else pseudo.Remove(":pointerover");
    }

    private static Border BlockIn(Border band) =>
        Of(band, "wholeZoneBlock").ShouldHaveSingleItem();

    private sealed record Look(double Thickness, IBrush? Outline);

    private static Look LookOf(Border block) =>
        new(block.BorderThickness.Top, block.BorderBrush);

    [AvaloniaFact]
    public void APictureDoesNotAnswerThePointer()
    {
        // The regression itself: hovering the middle band of a read-only diagram
        // thickened its outline, and the extra pixel pushed the chip and the
        // zone name as it grew.
        var window = Render(interactive: false);
        var bands = Of(window, "wholeZoneBand");

        bands.ShouldNotBeEmpty();

        foreach (var band in bands)
        {
            var block = BlockIn(band);
            var atRest = LookOf(block);

            Hover(band, true);
            Settle(window);

            LookOf(block).ShouldBe(atRest, "a diagram nobody is editing must not react to a pointer");
        }
    }

    [AvaloniaFact]
    public void AControlStillDoes()
    {
        // The other half of the pair, and the reason the first assertion is
        // worth anything: it proves a hover is observable through this harness
        // at all, so "nothing changed" above means the styles are gated rather
        // than the test being blind.
        var window = Render(interactive: true);
        var band = Of(window, "wholeZoneBand").First();
        var block = BlockIn(band);

        var atRest = LookOf(block);

        Hover(band, true);
        Settle(window);

        LookOf(block).ShouldNotBe(atRest, "an editable zone has to show what is about to be clicked");
    }

    [AvaloniaFact]
    public void TheDifferenceIsOnlyEverTheHover()
    {
        // At rest the two diagrams must look the same. Otherwise "does not react
        // to a pointer" could be satisfied by a read-only diagram that draws its
        // middle band permanently lit, which is the same false promise arrived
        // at from the other direction.
        var still = BlockIn(Of(Render(interactive: false), "wholeZoneBand").First());
        var live = BlockIn(Of(Render(interactive: true), "wholeZoneBand").First());

        still.BorderThickness.ShouldBe(live.BorderThickness);
    }
}
