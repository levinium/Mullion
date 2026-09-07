using Avalonia;
using Avalonia.Media;

namespace Mullion.App.Controls;

/// <summary>
/// Vector icons built in code, all on a 24x24 canvas.
/// <para>
/// Drawn as geometry rather than glyphs from an icon font: Segoe Fluent Icons is
/// not guaranteed to be present, and the Unicode gear renders as a color emoji on
/// some font stacks, which looks wrong beside a monochrome UI. A path is
/// deterministic, scales cleanly, and takes the theme's foreground brush.
/// </para>
/// <para>
/// The shared canvas is load-bearing. Rendered with Stretch=Uniform each icon is
/// scaled to FILL the box it is given, so a square cross came out visibly bigger
/// than a check, which is short and wide - the same 18px box, but one glyph
/// filling it and the other not. Drawn at a fixed size on one canvas instead,
/// each icon keeps the proportions it was designed with, and matching them is a
/// matter of drawing them to match rather than of what shape they happen to be.
/// </para>
/// </summary>
public static class Icons
{
    /// <summary>The canvas every icon is drawn on, and the size a Path is given.</summary>
    public const double Canvas = 24.0;

    private const double Center = 12.0;

    /// <summary>A cog with a hole through the middle.</summary>
    public static StreamGeometry Gear { get; } = BuildGear(
        teeth: 8, outerRadius: 9.4, rootRadius: 6.9, hubRadius: 3.1,
        toothTopHalfDegrees: 11.0, toothRootHalfDegrees: 21.0);

    /// <summary>A pencil lying at 45 degrees, for entering edit mode.</summary>
    public static StreamGeometry Pencil { get; } = BuildPencil();

    /// <summary>A tick, for keeping an edit.</summary>
    public static StreamGeometry Check { get; } = BuildCheck();

    /// <summary>A cross, for abandoning one.</summary>
    public static StreamGeometry Cross { get; } = BuildCross();

    /// <summary>An arrow curving back on itself, pointing left.</summary>
    public static StreamGeometry Undo { get; } = BuildCurvedArrow(pointsLeft: true);

    /// <summary>The same arrow mirrored, so the pair reads as one action and its reverse.</summary>
    public static StreamGeometry Redo { get; } = BuildCurvedArrow(pointsLeft: false);

    /// <summary>A horseshoe magnet, for snapping.</summary>
    public static StreamGeometry Magnet { get; } = BuildMagnet();

    private static StreamGeometry BuildGear(
        int teeth,
        double outerRadius,
        double rootRadius,
        double hubRadius,
        double toothTopHalfDegrees,
        double toothRootHalfDegrees)
    {
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

                var p1 = Polar(rootRadius, angle - toothRootHalfDegrees);
                var p2 = Polar(outerRadius, angle - toothTopHalfDegrees);
                var p3 = Polar(outerRadius, angle + toothTopHalfDegrees);
                var p4 = Polar(rootRadius, angle + toothRootHalfDegrees);

                if (i == 0) ctx.BeginFigure(p1, isFilled: true);
                else ctx.LineTo(p1);

                ctx.LineTo(p2);
                ctx.LineTo(p3);
                ctx.LineTo(p4);
            }

            ctx.EndFigure(isClosed: true);

            Circle(ctx, hubRadius);
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
            ctx.BeginFigure(new Point(5.4, 16.4), isFilled: true);
            ctx.LineTo(new Point(14.6, 7.2));
            ctx.LineTo(new Point(16.8, 9.4));
            ctx.LineTo(new Point(7.6, 18.6));
            ctx.EndFigure(isClosed: true);

            // The tip, running to a point: the reason it reads as a pencil and
            // not as a ruler.
            ctx.BeginFigure(new Point(4.4, 19.6), isFilled: true);
            ctx.LineTo(new Point(5.2, 17.0));
            ctx.LineTo(new Point(7.0, 18.8));
            ctx.EndFigure(isClosed: true);

            // The ferrule end, squared off across the top corner.
            ctx.BeginFigure(new Point(15.5, 6.3), isFilled: true);
            ctx.LineTo(new Point(17.0, 4.8));
            ctx.LineTo(new Point(19.2, 7.0));
            ctx.LineTo(new Point(17.7, 8.5));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildCheck()
    {
        var geometry = new StreamGeometry();

        // A stroked polyline would need a pen; every icon here is filled so they
        // all take one brush, so the tick is drawn as its own outline - down to
        // the elbow, up to the tip, and back.
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(5.0, 12.2), isFilled: true);
            ctx.LineTo(new Point(7.1, 10.1));
            ctx.LineTo(new Point(10.0, 13.0));
            ctx.LineTo(new Point(16.9, 6.1));
            ctx.LineTo(new Point(19.0, 8.2));
            ctx.LineTo(new Point(10.0, 17.2));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildCross()
    {
        var geometry = new StreamGeometry();

        // Deliberately smaller than its box. A cross reaches its corners in both
        // directions at once, so drawn to the same extents as everything else it
        // carries more ink than any of them and reads as oversized - which is
        // exactly how it looked beside the tick.
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(7.4, 6.0), isFilled: true);
            ctx.LineTo(new Point(12.0, 10.6));
            ctx.LineTo(new Point(16.6, 6.0));
            ctx.LineTo(new Point(18.0, 7.4));
            ctx.LineTo(new Point(13.4, 12.0));
            ctx.LineTo(new Point(18.0, 16.6));
            ctx.LineTo(new Point(16.6, 18.0));
            ctx.LineTo(new Point(12.0, 13.4));
            ctx.LineTo(new Point(7.4, 18.0));
            ctx.LineTo(new Point(6.0, 16.6));
            ctx.LineTo(new Point(10.6, 12.0));
            ctx.LineTo(new Point(6.0, 7.4));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>
    /// An arrow arcing over the top and turning back on itself, with the head on
    /// the side it points to. Undo and redo are the same shape mirrored, which is
    /// what makes them read as one action and its reverse rather than two arrows.
    /// </summary>
    private static StreamGeometry BuildCurvedArrow(bool pointsLeft)
    {
        const double Middle = 12.8;
        const double Outer = 7.2;
        const double Inner = 4.2;

        double X(double x) => pointsLeft ? x : Canvas - x;

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(X(Center - Outer), Middle), isFilled: true);

            ctx.ArcTo(
                new Point(X(Center + Outer), Middle),
                new Size(Outer, Outer),
                0,
                isLargeArc: false,
                pointsLeft ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);

            ctx.LineTo(new Point(X(Center + Inner), Middle));

            ctx.ArcTo(
                new Point(X(Center - Inner), Middle),
                new Size(Inner, Inner),
                0,
                isLargeArc: false,
                pointsLeft ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);

            ctx.EndFigure(isClosed: true);

            // The head hangs below the band's own end and is wider than it, so
            // the arrow has somewhere to be going.
            var tip = Center - (Outer + Inner) / 2;

            ctx.BeginFigure(new Point(X(tip - 3.7), Middle - 0.9), isFilled: true);
            ctx.LineTo(new Point(X(tip + 3.7), Middle - 0.9));
            ctx.LineTo(new Point(X(tip), Middle + 5.4));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildMagnet()
    {
        const double Outer = 7.0;
        const double Inner = 3.8;
        const double Top = 11.8;
        const double Foot = 19.2;

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // A horseshoe: round over the top, square-cut legs pointing down.
            ctx.BeginFigure(new Point(Center - Outer, Top), isFilled: true);

            ctx.ArcTo(
                new Point(Center + Outer, Top),
                new Size(Outer, Outer),
                0,
                isLargeArc: false,
                SweepDirection.Clockwise);

            ctx.LineTo(new Point(Center + Outer, Foot));
            ctx.LineTo(new Point(Center + Inner, Foot));
            ctx.LineTo(new Point(Center + Inner, Top));

            ctx.ArcTo(
                new Point(Center - Inner, Top),
                new Size(Inner, Inner),
                0,
                isLargeArc: false,
                SweepDirection.CounterClockwise);

            ctx.LineTo(new Point(Center - Inner, Foot));
            ctx.LineTo(new Point(Center - Outer, Foot));

            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static void Circle(StreamGeometryContext ctx, double radius)
    {
        ctx.BeginFigure(new Point(Center - radius, Center), isFilled: true);

        ctx.ArcTo(new Point(Center + radius, Center), new Size(radius, radius),
            0, isLargeArc: false, SweepDirection.Clockwise);

        ctx.ArcTo(new Point(Center - radius, Center), new Size(radius, radius),
            0, isLargeArc: false, SweepDirection.Clockwise);

        ctx.EndFigure(isClosed: true);
    }

    private static Point Polar(double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(Center + radius * Math.Cos(radians), Center + radius * Math.Sin(radians));
    }
}
