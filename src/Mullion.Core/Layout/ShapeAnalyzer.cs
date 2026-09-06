using Mullion.Core.Geometry;

namespace Mullion.Core.Layout;

/// <summary>How many ways a display can usefully be split along its long axis.</summary>
/// <param name="Min">Fewest splits before a zone is too wide.</param>
/// <param name="Max">Most splits before a zone is too narrow, or too few pixels.</param>
/// <param name="Preferred">The default choice within that range.</param>
public readonly record struct ZoneCountRange(int Min, int Max, int Preferred)
{
    public bool Allows(int n) => n >= Min && n <= Max;
}

/// <summary>
/// Derives split counts from pixels and aspect ratio alone.
/// <para>
/// The idea that removes the need for a class enum: don't ask what category a
/// display belongs to, ask what shape its zones would end up being. A zone is
/// usable when its own aspect ratio is a shape a window can live in.
/// </para>
/// </summary>
public static class ShapeAnalyzer
{
    /// <summary>
    /// Split counts for a display, computed along its LONG axis and therefore
    /// identical whether the panel is rotated or not. A 5120x1440 and a
    /// 1440x5120 yield the same range; only the split axis differs.
    /// </summary>
    /// <param name="bounds">Full display bounds in physical pixels.</param>
    /// <param name="dpi">Effective DPI (96 == 100% scaling).</param>
    /// <param name="hasOtherDisplays">
    /// When true, a display that can comfortably BE one zone stays whole -
    /// one hotkey per monitor. A lone display always subdivides, since a single
    /// monitor with a single zone is no tiling at all.
    /// </param>
    public static ZoneCountRange ZoneCounts(
        PxRect bounds,
        uint dpi,
        bool hasOtherDisplays,
        ShapeTuning? tuning = null)
    {
        var t = tuning ?? ShapeTuning.Default;

        if (bounds.IsEmpty) return new ZoneCountRange(1, 1, 1);

        var r = bounds.Elongation;

        // Fewest splits such that no zone is wider than ZoneAspectMax.
        var min = Math.Max(1, (int)Math.Ceiling(r / t.ZoneAspectMax - Epsilon));

        // Most splits before zones get too narrow by ratio...
        var maxByAspect = (int)Math.Floor(r / t.ZoneAspectMin + Epsilon);

        // ...or too small in absolute terms. DPI-scaled so a 150% display gets
        // the same apparent floor as a 100% one.
        var minZonePx = t.MinZoneLogicalPx * (dpi / 96.0);
        var maxByPixels = minZonePx <= 0 ? int.MaxValue : (int)Math.Floor(bounds.LongAxis / minZonePx);

        var max = Math.Max(min, Math.Min(maxByAspect, maxByPixels));

        return new ZoneCountRange(min, max, PreferredCount(bounds, min, max, hasOtherDisplays, t));
    }

    private const double Epsilon = 1e-9;

    private static int PreferredCount(
        PxRect bounds, int min, int max, bool hasOtherDisplays, ShapeTuning t)
    {
        // A display that can comfortably be one zone, alongside others, stays whole:
        // "a whole monitor is one hotkey" from the original spec.
        if (min == 1 && hasOtherDisplays) return 1;

        // Otherwise pick the count whose resulting zone shape sits closest to the
        // preferred aspect. This is what holds a 32:9 at three zones even in a
        // multi-monitor setup, and a 21:9 at two.
        var best = min;
        var bestDistance = double.MaxValue;

        for (var n = Math.Max(min, hasOtherDisplays ? min : 2); n <= max; n++)
        {
            var zoneAspect = ZoneAspectAt(bounds, n);
            var distance = Math.Abs(Math.Log(zoneAspect / t.PreferredZoneAspect));
            if (distance < bestDistance - Epsilon)
            {
                bestDistance = distance;
                best = n;
            }
        }

        return best;
    }

    /// <summary>Aspect (long/short of the resulting zone) when split into n equal parts.</summary>
    public static double ZoneAspectAt(PxRect bounds, int n)
    {
        if (n <= 0) return 0;
        var longSide = (double)bounds.LongAxis / n;
        var shortSide = (double)bounds.ShortAxis;
        return shortSide == 0 ? 0 : longSide / shortSide;
    }

    /// <summary>
    /// The axis a display subdivides along. Landscape splits into columns,
    /// portrait into rows - one generator, transposed.
    /// </summary>
    public static Axis SplitAxis(PxRect bounds) =>
        bounds.Height > bounds.Width ? Axis.Vertical : Axis.Horizontal;

    public static Orientation OrientationOf(PxRect bounds) =>
        bounds.Height > bounds.Width ? Orientation.Portrait : Orientation.Landscape;
}

public enum Orientation
{
    Landscape,
    Portrait,
}
