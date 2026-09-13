using Avalonia;
using Avalonia.Controls;
using Mullion.Core.Geometry;

namespace Mullion.App.Services;

/// <summary>
/// Puts an overlay window over a rectangle given in physical pixels.
/// <para>
/// Avalonia measures a window's POSITION in physical pixels and its SIZE in
/// device-independent ones. Zones are physical everywhere else in Mullion - they
/// come out of Win32 and go back into SetWindowPos - so handing the same numbers
/// to both stretched every overlay by its monitor's scale factor. On a 1920-wide
/// display at 150% the outline came out 2880 wide and ran most of the way across
/// the next monitor along, while the window it was supposed to be tracing landed
/// exactly right.
/// </para>
/// <para>
/// It looked correct for a long time because the desk it was written on runs at
/// 100%, where the two units are numerically identical and the bug is invisible.
/// </para>
/// </summary>
public static class OverlayPlacement
{
    /// <summary>Used when no screen can be identified, and for an odd scale.</summary>
    private const double Unscaled = 1.0;

    /// <summary>
    /// Position an overlay over a physical rectangle.
    /// <para>
    /// Position first, so the window is already on the monitor whose scale the
    /// size is then expressed in. The device-independent size is the durable
    /// quantity either way: Windows raises WM_DPICHANGED as a window crosses onto
    /// a differently scaled monitor and Avalonia keeps the DIP size across it, so
    /// the physical size converges on the target from whichever side it starts.
    /// </para>
    /// </summary>
    public static void PlaceAt(Window window, PxRect target)
    {
        window.Position = new PixelPoint(target.X, target.Y);

        var size = SizeFor(target, ScaleOf(window.Screens, target));

        window.Width = size.Width;
        window.Height = size.Height;
    }

    /// <summary>
    /// The device-independent size that covers <paramref name="target"/> on a
    /// monitor at <paramref name="scaling"/>.
    /// </summary>
    public static Size SizeFor(PxRect target, double scaling)
    {
        var scale = scaling > 0 ? scaling : Unscaled;

        // A zero-sized overlay is not a smaller mistake than an oversized one -
        // it is an invisible one, and it would read as the flash never firing.
        return new Size(
            Math.Max(1, target.Width) / scale,
            Math.Max(1, target.Height) / scale);
    }

    /// <summary>
    /// The scale of the monitor a rectangle sits on.
    /// <para>
    /// By the whole rectangle rather than its corner, so a zone spanning a bezel
    /// takes the scale of the monitor holding most of it instead of whichever one
    /// happens to own its top-left pixel.
    /// </para>
    /// </summary>
    public static double ScaleOf(Screens? screens, PxRect target)
    {
        if (screens is null) return Unscaled;

        var bounds = new PixelRect(
            target.X, target.Y, Math.Max(1, target.Width), Math.Max(1, target.Height));

        var screen = screens.ScreenFromBounds(bounds)
                     ?? screens.ScreenFromPoint(bounds.TopLeft)
                     ?? screens.Primary;

        var scale = screen?.Scaling ?? Unscaled;
        return scale > 0 ? scale : Unscaled;
    }
}
