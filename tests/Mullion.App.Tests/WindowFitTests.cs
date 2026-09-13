using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mullion.App.ViewModels;
using Avalonia.Controls.Presenters;
using Mullion.App.Views;
using Mullion.Core.Simulation;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Whether the windows still work on a screen that is not this one.
/// <para>
/// Resolution and scaling are not two variables here, they are one. Windows
/// hands an app its size in layout units, which is physical pixels divided by
/// the scaling factor, so a 1920x1080 display at 150% gives a window exactly as
/// much room as a 1280x720 display at 100%. Every case below is written as the
/// pair it comes from and reduced to the one number that decides whether a
/// control fits.
/// </para>
/// <para>
/// What is asserted is not that things look good but that they are THERE: a
/// control drawn outside the window it belongs to, or collapsed to nothing,
/// cannot be clicked, and the ones being checked are the only ways out of the
/// states this app puts you in - confirm an edit, cancel it, reach settings.
/// </para>
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowFitTests
{
    /// <summary>
    /// A screen worth supporting, and what it leaves an app in layout units.
    /// </summary>
    /// <param name="Name">How someone would describe the machine.</param>
    /// <param name="Width">Usable width in layout units.</param>
    /// <param name="Height">Usable height in layout units, taskbar removed.</param>
    public sealed record Screen(string Name, double Width, double Height);

    /// <summary>
    /// The range this has to survive, smallest first. A 48px taskbar and a 32px
    /// title bar come off the height, because a window cannot use either.
    /// </summary>
    public static TheoryData<string, double, double> Screens()
    {
        var data = new TheoryData<string, double, double>();

        foreach (var s in All) data.Add(s.Name, s.Width, s.Height);

        return data;
    }

    private static readonly Screen[] All =
    [
        // The floor of what ships on new hardware, and the case this was
        // written for: a 1080p laptop at the scaling Windows itself picks.
        new("1920x1080 at 150%", 1280, 720 - 48 - 32),
        new("1366x768 at 100%", 1366, 768 - 48 - 32),
        new("1280x800 at 100%", 1280, 800 - 48 - 32),
        new("1920x1080 at 125%", 1536, 864 - 48 - 32),
        new("2560x1440 at 150%", 1707, 960 - 48 - 32),
        new("1920x1080 at 100%", 1920, 1080 - 48 - 32),
        new("3840x2160 at 200%", 1920, 1080 - 48 - 32),
        new("5120x1440 at 100%", 5120, 1440 - 48 - 32),
    ];

    private static Window Lay(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.Show();

        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }

        return window;
    }

    private static MainWindow MainWindowFor()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new Mullion.App.Services.WindowsAppHost(SimulatedTopologies.Find("single-32-9"));
        host.Start();

        return new MainWindow { DataContext = new MainWindowViewModel(host) };
    }

    private static SettingsWindow SettingsWindowFor()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new Mullion.App.Services.WindowsAppHost(SimulatedTopologies.Find("single-32-9"));
        host.Start();

        return new SettingsWindow { DataContext = new SettingsViewModel(host) };
    }

    private static WizardWindow WizardWindowFor()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new Mullion.App.Services.WindowsAppHost(SimulatedTopologies.Find("single-32-9"));
        host.Start();

        return new WizardWindow { DataContext = new WizardViewModel(host) };
    }

    /// <summary>
    /// Anything a person has to be able to hit: buttons, boxes and the controls
    /// that carry a choice. Not labels - text that runs out of room is a
    /// legibility problem, and this is about reachability.
    /// <para>
    /// A scroll bar's own parts are excluded. They are Buttons, they sit inside
    /// the thing being measured, and a scroll bar with nothing to scroll leaves
    /// them at zero height - which is correct, and would otherwise report every
    /// screen as broken the moment the window learned to scroll.
    /// </para>
    /// </summary>
    private static IEnumerable<Control> Controls(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c is Button or CheckBox or ComboBox or ToggleButton)
            .Where(c => c.IsVisible && c.IsEffectivelyVisible)
            .Where(c => !c.GetVisualAncestors().OfType<ScrollBar>().Any());

    private static Rect BoundsIn(Visual visual, Visual root) =>
        visual.Bounds.TransformToAABB(
            visual.GetVisualParent()!.TransformToVisual(root) ?? Matrix.Identity);

    /// <summary>
    /// How much of a control its own ancestors cut away, as a fraction.
    /// <para>
    /// The check the window's own edges cannot make. A card clips what
    /// overflows it, so a toolbar pushed out of one is drawn nowhere and
    /// clickable nowhere while its bounds still sit happily inside the window -
    /// which is exactly how an edit session ended up with no way to confirm or
    /// cancel it on a short screen, with every other assertion here passing.
    /// </para>
    /// </summary>
    private static double ClippedAway(Control control, Visual root)
    {
        var box = BoundsIn(control, root);
        if (box.Width <= 0 || box.Height <= 0) return 1;

        var visible = box;

        foreach (var ancestor in control.GetVisualAncestors().OfType<Visual>())
        {
            if (!ancestor.ClipToBounds) continue;

            visible = visible.Intersect(BoundsIn(ancestor, root));

            if (visible.Width <= 0 || visible.Height <= 0) return 1;
        }

        return 1 - visible.Width * visible.Height / (box.Width * box.Height);
    }

    /// <summary>
    /// As <see cref="ClippedAway"/>, but forgiving the clip a scroll viewport
    /// performs: content below the fold is not lost, it is scrolled to.
    /// </summary>
    private static double ScrollableClippedAway(Control control, Visual root)
    {
        var box = BoundsIn(control, root);
        if (box.Width <= 0 || box.Height <= 0) return 1;

        var visible = box;

        foreach (var ancestor in control.GetVisualAncestors().OfType<Visual>())
        {
            if (ancestor is ScrollContentPresenter or ScrollViewer) break;
            if (!ancestor.ClipToBounds) continue;

            visible = visible.Intersect(BoundsIn(ancestor, root));

            if (visible.Width <= 0 || visible.Height <= 0) return 1;
        }

        return 1 - visible.Width * visible.Height / (box.Width * box.Height);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void EveryControlOnTheMainWindowIsInsideIt(string name, double width, double height)
    {
        var window = Lay(MainWindowFor(), width, height);

        foreach (var control in Controls(window))
        {
            var box = BoundsIn(control, window);

            box.Width.ShouldBeGreaterThan(0, $"{name}: a control collapsed to nothing");
            box.Height.ShouldBeGreaterThan(0, $"{name}: a control collapsed to nothing");

            box.Bottom.ShouldBeLessThanOrEqualTo(height + 1,
                $"{name}: a control runs off the bottom at {box}");
            box.Right.ShouldBeLessThanOrEqualTo(width + 1,
                $"{name}: a control runs off the right at {box}");

            ClippedAway(control, window).ShouldBeLessThan(0.05,
                $"{name}: a control is cut away by what contains it, at {box}");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void TheUpdateControlsFitWhileAnUpdateIsInFlight(string name, double width, double height)
    {
        // Every one of these is hidden until an update is actually being
        // installed, so the ordinary settings-window test measures a row that
        // never contains them. This is the state nothing else looks at: the
        // longest the row ever gets, plus the progress bar underneath it.
        var window = SettingsWindowFor();
        var vm = (SettingsViewModel)window.DataContext!;

        vm.CanSelfUpdate = true;
        vm.UpdateUrl = "https://example.invalid/release";
        vm.UpdateStaged = true;
        vm.IsDownloadingUpdate = true;
        vm.UpdateProgress = 0.5;
        vm.UpdateMessage = "Downloaded and verified. Restart to finish.";

        Lay(window, width, height);

        foreach (var control in Controls(window))
        {
            var box = BoundsIn(control, window);

            box.Width.ShouldBeGreaterThan(0, $"{name}: a control collapsed to nothing");
            box.Right.ShouldBeLessThanOrEqualTo(width + 1,
                $"{name}: a control runs off the right at {box}");

            ScrollableClippedAway(control, window).ShouldBeLessThan(0.05,
                $"{name}: a control is cut away by what contains it, at {box}");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void TheUpdateNoticeFitsToo(string name, double width, double height)
    {
        // The notice is hidden unless a newer release exists, so every other fit
        // test measures the row WITHOUT it - which means the one arrangement
        // that could overflow the top row is the one nothing checks. The text
        // is as long as it ever gets here.
        var window = MainWindowFor();
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.NewerVersion = "10.10.10";
        vm.NewerVersionUrl = "https://example.invalid/release";

        Lay(window, width, height);

        foreach (var control in Controls(window))
        {
            var box = BoundsIn(control, window);

            box.Width.ShouldBeGreaterThan(0, $"{name}: a control collapsed to nothing");
            box.Right.ShouldBeLessThanOrEqualTo(width + 1,
                $"{name}: a control runs off the right at {box}");

            ClippedAway(control, window).ShouldBeLessThan(0.05,
                $"{name}: a control is cut away by what contains it, at {box}");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void TheEditToolbarSurvivesEveryScreen(string name, double width, double height)
    {
        // The way out of edit mode. Off the bottom of the window it is not
        // merely ugly - there is no way to keep or discard what you changed.
        var window = Lay(MainWindowFor(), width, height);
        var model = (MainWindowViewModel)window.DataContext!;

        model.Editor.BeginEditCommand.Execute(null);

        for (var pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }

        var icons = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("iconButton") && b.IsEffectivelyVisible)
            .ToList();

        icons.Count.ShouldBeGreaterThan(4, $"{name}: the edit controls are not being drawn");

        foreach (var icon in icons)
        {
            var box = BoundsIn(icon, window);

            box.Height.ShouldBeGreaterThan(0, $"{name}: an edit control collapsed");
            box.Bottom.ShouldBeLessThanOrEqualTo(height + 1,
                $"{name}: an edit control is off the bottom at {box}");

            ClippedAway(icon, window).ShouldBeLessThan(0.05,
                $"{name}: an edit control is cut away by what contains it, at {box}");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void EveryControlInSettingsIsReachable(string name, double width, double height)
    {
        // Settings scrolls, so nothing here is about running past the bottom -
        // it is about a control being cut away by a card while the scroll bar
        // reports there is nothing more to see.
        var window = Lay(SettingsWindowFor(), width, height);

        foreach (var control in Controls(window))
        {
            var box = BoundsIn(control, window);

            box.Width.ShouldBeGreaterThan(0, $"{name}: a settings control collapsed to nothing");
            box.Height.ShouldBeGreaterThan(0, $"{name}: a settings control collapsed to nothing");

            ScrollableClippedAway(control, window).ShouldBeLessThan(0.05,
                $"{name}: a settings control is cut away by what contains it, at {box}");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void TheWizardsWayForwardIsAlwaysReachable(string name, double width, double height)
    {
        // Back and Next are the only way through setup, and setup is the first
        // thing a new install shows. Off the bottom, the app is unusable before
        // it has been used once.
        var window = Lay(WizardWindowFor(), width, height);

        var buttons = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Content is string s &&
                        (s.StartsWith("Next") || s.StartsWith("Back")))
            .ToList();

        buttons.Count.ShouldBe(2, $"{name}: the wizard's navigation is not being drawn");

        foreach (var button in buttons)
        {
            var box = BoundsIn(button, window);

            box.Height.ShouldBeGreaterThan(0, $"{name}: wizard navigation collapsed");
            box.Bottom.ShouldBeLessThanOrEqualTo(height + 1,
                $"{name}: wizard navigation is off the bottom at {box}");

            ClippedAway(button, window).ShouldBeLessThan(0.05,
                $"{name}: wizard navigation is cut away at {box}");
        }
    }

    /// <summary>The sizes the three windows ask for in XAML.</summary>
    public static TheoryData<string, double, double> Wanted() => new()
    {
        { "main", 1100, 720 },
        { "settings", 720, 760 },
        { "wizard", 1080, 720 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Wanted))]
    public void NoWindowOpensBiggerThanTheScreenItOpensOn(string which, double width, double height)
    {
        // A window that opens taller than the screen puts its own bottom edge
        // out of reach, and on Windows that includes whatever sits along it.
        // Every one of the three asks for more than a 1080p laptop at 150%
        // has, so every one of them has to be shrunk on the way up.
        foreach (var screen in All)
        {
            var fitted = ScreenFitProbe(new Size(width, height), new Size(screen.Width, screen.Height));

            fitted.Width.ShouldBeLessThanOrEqualTo(screen.Width,
                $"{which} does not fit {screen.Name}");
            fitted.Height.ShouldBeLessThanOrEqualTo(screen.Height,
                $"{which} does not fit {screen.Name}");

            fitted.Width.ShouldBeLessThanOrEqualTo(width, $"{which} was made wider than it asked for");
            fitted.Height.ShouldBeLessThanOrEqualTo(height, $"{which} was made taller than it asked for");
        }
    }

    [AvaloniaFact]
    public void AWindowThatAlreadyFitsIsLeftAlone()
    {
        // Shrinking to the screen must not become shrinking on principle: on a
        // display with room to spare the window opens at the size it declared.
        var roomy = new Size(3000, 2000);

        ScreenFitProbe(new Size(1100, 720), roomy).ShouldBe(new Size(1100, 720));
    }

    [AvaloniaFact]
    public void EveryWindowsMinimumFitsTheSmallestScreen()
    {
        // The floor matters as much as the size: a window whose minimum is
        // taller than the screen cannot be dragged small enough to fit, however
        // hard anyone tries.
        var smallest = All.MinBy(s => s.Height)!;
        var narrowest = All.MinBy(s => s.Width)!;

        foreach (var (name, minWidth, minHeight) in new[]
                 {
                     ("main", 720d, 520d),
                     ("settings", 600d, 560d),
                     ("wizard", 820d, 560d),
                 })
        {
            minHeight.ShouldBeLessThanOrEqualTo(smallest.Height,
                $"{name}'s minimum height does not fit {smallest.Name}");
            minWidth.ShouldBeLessThanOrEqualTo(narrowest.Width,
                $"{name}'s minimum width does not fit {narrowest.Name}");
        }
    }

    [AvaloniaFact]
    public void FittingHappensOnceAndNotEveryTimeTheWindowComesBack()
    {
        // The main window is hidden to the tray and shown from it rather than
        // being recreated. Width is still whatever XAML asked for however the
        // user has since dragged the edges, so fitting on every open would
        // quietly undo their resize each time they brought it back.
        var window = MainWindowFor();
        window.Show();

        // Bigger than any screen the headless platform reports, so a second
        // fitting would have to shrink it and the difference would show.
        window.Width = 4000;
        window.Height = 3000;

        window.Hide();
        window.Show();

        window.Width.ShouldBe(4000, "the window was re-fitted on a later open");
        window.Height.ShouldBe(3000, "the window was re-fitted on a later open");
    }

    [AvaloniaFact]
    public void AShrunkWindowIsPulledBackOntoTheScreen()
    {
        // The trap in shrinking alone. A window is centered before it is
        // measured, so one 720 tall centered on a screen with 640 starts above
        // the top; shrink it where it stands and the title bar - the one part
        // you would grab to move it - is still off the screen.
        var screen = new PixelRect(0, 0, 1280, 640);
        var centeredTooHigh = new PixelRect(0, -40, 1280, 592);

        var moved = NudgeProbe(centeredTooHigh, screen);

        moved.Y.ShouldBeGreaterThanOrEqualTo(0, "the title bar must be reachable");
        (moved.Y + 592).ShouldBeLessThanOrEqualTo(640, "and the bottom edge must be too");
    }

    [AvaloniaFact]
    public void AWindowAlreadyOnTheScreenIsNotMoved()
    {
        var screen = new PixelRect(0, 0, 1920, 1000);
        var fine = new PixelRect(400, 100, 1100, 720);

        NudgeProbe(fine, screen).ShouldBe(new PixelPoint(400, 100));
    }

    [AvaloniaFact]
    public void AWindowOnASecondMonitorStaysOnIt()
    {
        // Work areas do not start at zero. A screen to the right of the primary
        // has a positive origin, and clamping to 0 would fling the window back
        // to the other monitor.
        var second = new PixelRect(1920, 0, 1920, 1000);
        var onIt = new PixelRect(2000, 50, 1100, 720);

        NudgeProbe(onIt, second).ShouldBe(new PixelPoint(2000, 50));
    }

    private static PixelPoint NudgeProbe(PixelRect window, PixelRect area) =>
        (PixelPoint)typeof(MainWindow).Assembly
            .GetType("Mullion.App.Views.ScreenFit")!
            .GetMethod("Nudge")!
            .Invoke(null, [window, area])!;

    /// <summary>
    /// The clamp's arithmetic, reached without a screen. Screens are what this
    /// whole file is about not having.
    /// </summary>
    private static Size ScreenFitProbe(Size wanted, Size available) =>
        (Size)typeof(MainWindow).Assembly
            .GetType("Mullion.App.Views.ScreenFit")!
            .GetMethod("Within")!
            .Invoke(null, [wanted, available])!;
}
