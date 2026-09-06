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
