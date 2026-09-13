using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Whether a settings page can be scrolled past a dropdown without changing it.
/// <para>
/// The settings window is one long ScrollViewer with four ComboBoxes in it, and
/// one of them picks the key surface - the control that decides which physical
/// keys every zone answers to. If a closed ComboBox takes the wheel, then
/// scrolling the page with the pointer anywhere over that row silently rebinds
/// every hotkey the machine has, with no dialog and nothing to undo. That is
/// indistinguishable from the app changing its own settings, which is exactly
/// how it was reported: the key surface had become the numpad and nobody had
/// touched it.
/// </para>
/// </summary>
public class ScrollSafetyTests
{
    private static readonly Size Canvas = new(400, 300);

    /// <summary>The three surfaces, in the order the settings dropdown lists them.</summary>
    private static ComboBox Surfaces() => new()
    {
        ItemsSource = new[] { "left-hand", "numpad", "right-hand" },
        SelectedIndex = 0,
        Width = 200,
    };

    private static (Window Window, ComboBox Box) Page()
    {
        var box = Surfaces();
        var window = new Window
        {
            Width = Canvas.Width,
            Height = Canvas.Height,
            Content = new ScrollViewer { Content = new StackPanel { Children = { box } } },
        };

        window.Show();
        window.Measure(Canvas);
        window.Arrange(new Rect(Canvas));
        Dispatcher.UIThread.RunJobs();

        return (window, box);
    }

    /// <summary>
    /// A wheel notch delivered to the dropdown itself, which is what happens
    /// when the pointer is over it and the page is scrolled.
    /// </summary>
    private static void Wheel(Window window, ComboBox box, double delta)
    {
        box.RaiseEvent(new PointerWheelEventArgs(
            box,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            window,
            box.Bounds.Center,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            new Vector(0, delta))
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent,
        });

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ScrollingOverAClosedDropdownDoesNotChangeIt()
    {
        var (window, box) = Page();

        Wheel(window, box, -1);
        Wheel(window, box, -1);

        box.SelectedIndex.ShouldBe(0,
            "scrolling a settings page must not rebind every hotkey on the machine");
    }

    [AvaloniaFact]
    public void ScrollingUpOverItDoesNotEither()
    {
        var (window, box) = Page();
        box.SelectedIndex = 1;

        Wheel(window, box, 1);

        box.SelectedIndex.ShouldBe(1);
    }

    [AvaloniaFact]
    public void TheWheelEventIsRealEnoughToScrollWith()
    {
        // Without this the two assertions above are worth nothing: a synthetic
        // event that no control ever sees would satisfy "the dropdown did not
        // change" no matter how the dropdown behaved. Raising it on the same
        // element and watching the page underneath actually move is what makes
        // the difference between "ComboBox ignores the wheel" and "this test
        // cannot tell".
        var box = Surfaces();
        var scroller = new ScrollViewer
        {
            Height = 60,
            Content = new StackPanel
            {
                Children = { box, new Border { Height = 600 } },
            },
        };

        var window = new Window { Width = Canvas.Width, Height = Canvas.Height, Content = scroller };
        window.Show();
        window.Measure(Canvas);
        window.Arrange(new Rect(Canvas));
        Dispatcher.UIThread.RunJobs();

        scroller.Offset.Y.ShouldBe(0);

        Wheel(window, box, -1);

        scroller.Offset.Y.ShouldBeGreaterThan(0,
            "the wheel event has to reach the page for the dropdown assertions to mean anything");
    }
}
