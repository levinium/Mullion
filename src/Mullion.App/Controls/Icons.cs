using Avalonia;
using Avalonia.Media;

namespace Mullion.App.Controls;

/// <summary>
/// Vector icons built in code.
/// <para>
/// Drawn as geometry rather than glyphs from an icon font: Segoe Fluent Icons
/// is not guaranteed to be present, and the Unicode gear renders as a color
/// emoji on some font stacks, which looks wrong beside a monochrome UI. A path
/// is deterministic, scales cleanly, and takes the theme's foreground brush.
/// </para>
/// </summary>
public static class Icons
{
    /// <summary>A cog on a 24x24 canvas, with a hole through the middle.</summary>
    public static StreamGeometry Gear { get; } = BuildGear(
        teeth: 8, outerRadius: 11.0, rootRadius: 8.0, hubRadius: 3.7,
        toothTopHalfDegrees: 11.0, toothRootHalfDegrees: 21.0);


    /// <summary>
    /// A pencil on the same 24x24 canvas as the gear, lying at 45 degrees.
    /// <para>
    /// Built from the same primitives for the same reason: it sits directly
    /// beside the gear, and a glyph from a font that may not be installed would
    /// be the one icon of the pair that did not match.
    /// </para>
    /// </summary>
    public static StreamGeometry Pencil { get; } = BuildPencil();

    /// <summary>A tick on the same 24x24 canvas, for confirming an edit.</summary>
    public static StreamGeometry Check { get; } = BuildCheck();

    /// <summary>A cross on the same canvas, for abandoning one.</summary>
    public static StreamGeometry Cross { get; } = BuildCross();

    private static StreamGeometry BuildCheck()
    {
        var geometry = new StreamGeometry();

        // A stroked polyline would need a pen; every icon here is a filled
        // path so they all take the same brush, so the tick is drawn as its
        // own outline - down to the elbow, up to the tip, and back.
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(4.2, 12.4), isFilled: true);
            ctx.LineTo(new Point(6.6, 10.0));
            ctx.LineTo(new Point(9.8, 13.2));
            ctx.LineTo(new Point(17.4, 5.6));
            ctx.LineTo(new Point(19.8, 8.0));
            ctx.LineTo(new Point(9.8, 18.0));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildCross()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // Twelve corners of two crossed bars, walked as one outline.
            ctx.BeginFigure(new Point(6.4, 4.6), isFilled: true);
            ctx.LineTo(new Point(12.0, 10.2));
            ctx.LineTo(new Point(17.6, 4.6));
            ctx.LineTo(new Point(19.4, 6.4));
            ctx.LineTo(new Point(13.8, 12.0));
            ctx.LineTo(new Point(19.4, 17.6));
            ctx.LineTo(new Point(17.6, 19.4));
            ctx.LineTo(new Point(12.0, 13.8));
            ctx.LineTo(new Point(6.4, 19.4));
            ctx.LineTo(new Point(4.6, 17.6));
            ctx.LineTo(new Point(10.2, 12.0));
            ctx.LineTo(new Point(4.6, 6.4));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildPencil()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // The body: a bar from the lower left to the upper right, drawn as
            // its four corners rather than a stroked line so it takes a fill
            // like every other icon here.
            ctx.BeginFigure(new Point(4.6, 17.0), isFilled: true);
            ctx.LineTo(new Point(15.1, 6.5));
            ctx.LineTo(new Point(17.5, 8.9));
            ctx.LineTo(new Point(7.0, 19.4));
            ctx.LineTo(new Point(4.6, 21.8));
            ctx.EndFigure(isClosed: true);

            // The tip, as a separate wedge running to a point: the whole reason
            // the shape reads as a pencil and not as a ruler.
            ctx.BeginFigure(new Point(3.2, 20.8), isFilled: true);
            ctx.LineTo(new Point(4.2, 17.8));
            ctx.LineTo(new Point(6.2, 19.8));
            ctx.EndFigure(isClosed: true);

            // The ferrule end, squared off across the top corner.
            ctx.BeginFigure(new Point(16.2, 5.4), isFilled: true);
            ctx.LineTo(new Point(18.0, 3.6));
            ctx.LineTo(new Point(20.4, 6.0));
            ctx.LineTo(new Point(18.6, 7.8));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildGear(
        int teeth,
        double outerRadius,
        double rootRadius,
        double hubRadius,
        double toothTopHalfDegrees,
        double toothRootHalfDegrees)
    {
        const double Center = 12.0;
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // EvenOdd is what turns the hub figure into a hole rather than
            // filling over it.
            ctx.SetFillRule(FillRule.EvenOdd);

            var step = 360.0 / teeth;

            // Trapezoid teeth: wider at the root than the tip, which is what
            // makes it read as a cog rather than a star at small sizes.
            for (var i = 0; i < teeth; i++)
            {
                var angle = i * step;

                var p1 = Polar(Center, rootRadius, angle - toothRootHalfDegrees);
                var p2 = Polar(Center, outerRadius, angle - toothTopHalfDegrees);
                var p3 = Polar(Center, outerRadius, angle + toothTopHalfDegrees);
                var p4 = Polar(Center, rootRadius, angle + toothRootHalfDegrees);

                if (i == 0) ctx.BeginFigure(p1, isFilled: true);
                else ctx.LineTo(p1);

                ctx.LineTo(p2);
                ctx.LineTo(p3);
                ctx.LineTo(p4);
            }

            ctx.EndFigure(isClosed: true);

            // The hub, as a separate figure. EvenOdd turns it into a hole.
            ctx.BeginFigure(new Point(Center + hubRadius, Center), isFilled: true);
            ctx.ArcTo(new Point(Center - hubRadius, Center),
                new Size(hubRadius, hubRadius), 0, false, SweepDirection.Clockwise);
            ctx.ArcTo(new Point(Center + hubRadius, Center),
                new Size(hubRadius, hubRadius), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Point Polar(double center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
    }
}
