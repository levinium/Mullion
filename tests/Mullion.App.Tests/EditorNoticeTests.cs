using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mullion.App.Controls;
using Mullion.App.ViewModels;
using Mullion.Core.Simulation;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Where the editor's messages are drawn, which is a question about layout
/// rather than about wording.
/// <para>
/// They used to appear on a line of their own above the diagram. Every message
/// therefore pushed the picture down and shrank it - including, absurdly,
/// "Changes discarded.", which moved the whole layout to announce that nothing
/// had changed, and the instruction shown for the entire time a key is being
/// rebound, which shrank the diagram you were rebinding it on.
/// </para>
/// <para>
/// They live in the toolbar row now, whose height the icons already set. The
/// property that matters is not that they look right but that they cost
/// nothing, so that is what is asserted.
/// </para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class EditorNoticeTests
{
    private static readonly Size Canvas = new(1040, 420);

    /// <summary>The editor for a real arrangement, laid out and settled.</summary>
    private static (ZoneEditor View, ZoneEditorViewModel Model, Window Window) Render()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new Mullion.App.Services.WindowsAppHost(SimulatedTopologies.Find("single-32-9"));
        host.Start();

        var model = new ZoneEditorViewModel(host);
        var view = new ZoneEditor { DataContext = model };

        var window = new Window { Width = Canvas.Width, Height = Canvas.Height, Content = view };
        window.Show();

        Settle(window);

        return (view, model, window);
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(Canvas);
            window.Arrange(new Rect(Canvas));
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Top edge of the drawn desk, in the editor's own coordinates.</summary>
    private static double DiagramTop(ZoneEditor view) =>
        view.GetVisualDescendants()
            .OfType<MonitorDiagram>()
            .Single()
            .Bounds.Y;

    [AvaloniaFact]
    public void AnInstructionDoesNotMoveTheDiagram()
    {
        // The longest thing the editor ever says, and it is up for the whole
        // time a key is being rebound.
        var (view, vm, window) = Render();

        var before = DiagramTop(view);

        vm.Message = "Hold the modifier and press the key you want for this zone. Click it again to cancel.";
        Settle(window);

        DiagramTop(view).ShouldBe(before, 0.5);
    }

    [AvaloniaFact]
    public void AConfirmationDoesNotMoveTheDiagramEither()
    {
        var (view, vm, window) = Render();

        var before = DiagramTop(view);

        vm.Flash = "Changes discarded.";
        Settle(window);

        DiagramTop(view).ShouldBe(before, 0.5);
    }

    [AvaloniaFact]
    public void EvenAnAbsurdlyLongRefusalDoesNotMoveTheDiagram()
    {
        // Trimming is what keeps this true, so a message far longer than any
        // the app produces is the case that proves the trimming is on.
        var (view, vm, window) = Render();

        var before = DiagramTop(view);

        vm.Message = string.Join(" ", Enumerable.Repeat("That key is not part of this block.", 12));
        Settle(window);

        DiagramTop(view).ShouldBe(before, 0.5);
    }

    [AvaloniaFact]
    public void TheTwoNoticesAreDrawnTheSameWay()
    {
        // One voice. There were two looks for the same thing - a tinted card
        // above the diagram and bare text in the toolbar - and which one you
        // got depended only on how long the message was going to stay.
        var (view, vm, window) = Render();

        vm.Message = "Hold the modifier and press the key you want.";
        Settle(window);
        var sticky = Notice(view);

        vm.Message = null;
        vm.Flash = "Changes discarded.";
        Settle(window);
        var transient = Notice(view);

        transient.Background.ShouldBe(sticky.Background);
        transient.BorderBrush.ShouldBe(sticky.BorderBrush);
        transient.CornerRadius.ShouldBe(sticky.CornerRadius);
        transient.Padding.ShouldBe(sticky.Padding);
    }

    private static Border Notice(ZoneEditor view) =>
        view.GetVisualDescendants()
            .OfType<Border>()
            .Single(b => b.Classes.Contains("editorNotice") && b.IsVisible && b.Bounds.Width > 0);
}
