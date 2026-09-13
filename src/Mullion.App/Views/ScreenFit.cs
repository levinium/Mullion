using Avalonia;
using Avalonia.Controls;

namespace Mullion.App.Views;

/// <summary>
/// Keeps a window inside the screen it opens on.
/// <para>
/// A window declares the size it would like in XAML, and that size is chosen
/// against whatever screen the person writing it happened to have. Windows does
/// not shrink it to fit: a 720-unit-tall window on a 1080p laptop at 150%
/// scaling - which leaves 640 - opens with its own bottom edge below the
/// taskbar, taking with it whatever was down there.
/// </para>
/// <para>
/// Resolution and scaling are one variable here, not two. An app is given its
/// size in layout units, which is physical pixels divided by the scaling
/// factor, so 1920x1080 at 150% and 1280x720 at 100% are the same room.
/// </para>
/// </summary>
internal static class ScreenFit
{
    /// <summary>
    /// Windows still waiting to be fitted. A set rather than a flag on the
    /// window because this is an extension method and the windows it serves
    /// have nothing else in common.
    /// </summary>
    private static readonly HashSet<Window> Pending = [];

    /// <summary>Ask to be fitted the next time this window opens.</summary>
    public static void FitWhenOpened(this Window window)
    {
        Pending.Add(window);
        window.Closed += (_, _) => Pending.Remove(window);
    }

    /// <summary>
    /// Room left for the window's own frame and for not looking wedged into
    /// the corner of the screen.
    /// </summary>
    private const double Chrome = 48;

    /// <summary>
    /// The largest of a wanted size that will actually fit, never larger than
    /// what was wanted.
    /// <para>
    /// Separated from the window so it can be checked against screens nobody
    /// here owns, which is the entire point of the exercise.
    /// </para>
    /// </summary>
    public static Size Within(Size wanted, Size available)
    {
        if (available.Width <= 0 || available.Height <= 0) return wanted;

        return new Size(
            Math.Min(wanted.Width, Math.Max(1, available.Width - Chrome)),
            Math.Min(wanted.Height, Math.Max(1, available.Height - Chrome)));
    }

    /// <summary>
    /// A window's top-left corner, moved the least it can be to put the whole
    /// window on the screen.
    /// <para>
    /// Shrinking alone is not enough and can make things worse. A window is
    /// centered before it is measured, so one 720 units tall centered on a
    /// screen with 640 starts 40 units ABOVE the top; shrink it in place and it
    /// is smaller, still hanging off the top, and now has its title bar out of
    /// reach - which is the one part you would use to drag it back.
    /// </para>
    /// </summary>
    public static PixelPoint Nudge(PixelRect window, PixelRect area)
    {
        // Top-left wins when the window is larger than the screen in some
        // direction: an edge has to go, and it must not be this one.
        var x = Math.Min(Math.Max(window.X, area.X), Math.Max(area.X, area.Right - window.Width));
        var y = Math.Min(Math.Max(window.Y, area.Y), Math.Max(area.Y, area.Bottom - window.Height));

        return new PixelPoint(x, y);
    }

    /// <summary>
    /// Shrink this window to the screen it is opening on, and lower its floor
    /// to match.
    /// <para>
    /// The minimum matters as much as the size. A window whose MinHeight is
    /// taller than the screen cannot be dragged small enough to fit however
    /// hard anyone tries, so a floor set for a comfortable layout becomes a
    /// window with no bottom edge.
    /// </para>
    /// </summary>
    public static void ClampToScreen(this Window window)
    {
        // Once, on the way up, and never again. The main window is hidden to
        // the tray and shown from it rather than being recreated, and Width is
        // whatever XAML asked for whichever way the user has since dragged the
        // edges - so running this a second time would quietly undo their resize
        // every time they brought the window back.
        if (!Pending.Remove(window)) return;

        var screen = window.Screens?.ScreenFromWindow(window) ?? window.Screens?.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;

        var available = new Size(
            screen.WorkingArea.Width / scaling,
            screen.WorkingArea.Height / scaling);

        var fitted = Within(new Size(window.Width, window.Height), available);

        window.MinWidth = Math.Min(window.MinWidth, fitted.Width);
        window.MinHeight = Math.Min(window.MinHeight, fitted.Height);

        window.Width = fitted.Width;
        window.Height = fitted.Height;

        var size = PixelSize.FromSize(fitted, scaling);

        window.Position = Nudge(
            new PixelRect(window.Position, size),
            screen.WorkingArea);
    }
}
