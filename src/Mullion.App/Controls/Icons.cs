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

    /// <summary>Two walls with arrows pushing outward onto them, for snapping.</summary>
    public static StreamGeometry Snap { get; } = BuildSnap();

    /// <summary>A split display with a reset arrow filling one of its panes.</summary>
    public static StreamGeometry ResetZones { get; } = BuildResetZones();

    /// <summary>A keycap carrying a reset arrow as its legend.</summary>
    public static StreamGeometry ResetKeys { get; } = BuildResetKeys();

    /// <summary>A ring arrow, for rescanning the displays.</summary>
    public static StreamGeometry Refresh { get; } = BuildRefresh();

    /// <summary>Two bars, for pausing.</summary>
    public static StreamGeometry Pause { get; } = BuildPause();

    /// <summary>A triangle, for resuming.</summary>
    public static StreamGeometry Play { get; } = BuildPlay();

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
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

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
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

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
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

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
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

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
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

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

    /// <summary>
    /// A ring most of the way round with an arrowhead on the open end. A ring
    /// rather than the half-arc the undo icon uses: the two sit in the same
    /// window and have to be tellable apart at a glance.
    /// </summary>
    private static StreamGeometry BuildRefresh()
    {
        const double Outer = 8.2;
        const double Inner = 5.6;

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

            var start = Polar(Outer, -55);
            var end = Polar(Outer, 195);
            var innerStart = Polar(Inner, 195);
            var innerEnd = Polar(Inner, -55);

            ctx.BeginFigure(start, isFilled: true);
            ctx.ArcTo(end, new Size(Outer, Outer), 0, isLargeArc: true, SweepDirection.Clockwise);
            ctx.LineTo(innerStart);
            ctx.ArcTo(innerEnd, new Size(Inner, Inner), 0, isLargeArc: true, SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);

            // The head sits across the open end and overhangs the band on both
            // sides, so the ring reads as going somewhere.
            var tip = Polar((Outer + Inner) / 2, -55);

            ctx.BeginFigure(new Point(tip.X - 3.4, tip.Y - 1.6), isFilled: true);
            ctx.LineTo(new Point(tip.X + 3.4, tip.Y - 1.6));
            ctx.LineTo(new Point(tip.X, tip.Y + 4.4));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildPause()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

            Bar(ctx, 7.2, 5.4, 10.2, 18.6);
            Bar(ctx, 13.8, 5.4, 16.8, 18.6);
        }

        return geometry;
    }

    private static StreamGeometry BuildPlay()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

            // Set slightly right of centre: a triangle balances on its area, not
            // its bounding box, and centred by the box it looks to be leaning back.
            ctx.BeginFigure(new Point(7.8, 5.0), isFilled: true);
            ctx.LineTo(new Point(18.4, 12.0));
            ctx.LineTo(new Point(7.8, 19.0));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry BuildSnap()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            Bar(ctx, 3.2, 4.6, 5.2, 19.4);
            Bar(ctx, 18.8, 4.6, 20.8, 19.4);

            // The heads overlap the shaft rather than being butted up against
            // it. Wound the same way they simply merge, and a seam avoided by a
            // tenth of a pixel is a seam that comes back at some other size.
            var shaft = 5.6;

            Bar(ctx, 12.0 - shaft / 2, 11.0, 12.0 + shaft / 2, 13.0);

            var reach = 5.8;

            Triangle(ctx,
                new Point(12.0 - reach, 12.0),
                new Point(12.0 - reach + 3.6, 8.4),
                new Point(12.0 - reach + 3.6, 15.6));

            Triangle(ctx,
                new Point(12.0 + reach, 12.0),
                new Point(12.0 + reach - 3.6, 8.4),
                new Point(12.0 + reach - 3.6, 15.6));
        }

        return geometry;
    }
    private static void ResetArrow(StreamGeometryContext ctx, double cx, double cy, double size)
    {
        var outer = size / 2;
        var inner = outer * 0.56;
        var mid = (outer + inner) / 2;
        var band = outer - inner;

        // The gap sits over the top, so the head comes to rest at the upper
        // right - where a reset arrow is usually read from.
        const double Head = -55.0;
        const double Tail = 205.0;

        ctx.BeginFigure(PolarAt(cx, cy, outer, Head), isFilled: true);
        ctx.ArcTo(PolarAt(cx, cy, outer, Tail), new Size(outer, outer),
            0, isLargeArc: true, SweepDirection.Clockwise);
        ctx.LineTo(PolarAt(cx, cy, inner, Tail));
        ctx.ArcTo(PolarAt(cx, cy, inner, Head), new Size(inner, inner),
            0, isLargeArc: true, SweepDirection.CounterClockwise);
        ctx.EndFigure(isClosed: true);

        // The head is built from the ring's own directions at that angle, not
        // from offsets in screen axes. Written flat, it only lines up for one
        // particular angle and juts off the side of the band at every other -
        // which is exactly what it did.
        var radians = Head * Math.PI / 180.0;

        // Along the radius, and along the circle against the direction of
        // travel, so the head reads as having come round to a stop here.
        var outwardX = Math.Cos(radians);
        var outwardY = Math.Sin(radians);
        var alongX = Math.Sin(radians);
        var alongY = -Math.Cos(radians);

        var width = band * 0.95;
        var length = band * 1.9;

        Triangle(ctx,
            new Point(cx + (mid + width) * outwardX, cy + (mid + width) * outwardY),
            new Point(cx + (mid - width) * outwardX, cy + (mid - width) * outwardY),
            new Point(
                cx + mid * outwardX + length * alongX,
                cy + mid * outwardY + length * alongY));
    }
    private static void RoundedFrame(
        StreamGeometryContext ctx,
        double left, double top, double right, double bottom,
        double radius, double thickness)
    {
        RoundedRect(ctx, left, top, right, bottom, radius, hole: false);

        RoundedRect(ctx,
            left + thickness, top + thickness, right - thickness, bottom - thickness,
            Math.Max(0.4, radius - thickness), hole: true);
    }
    private static void RoundedRect(
        StreamGeometryContext ctx,
        double left, double top, double right, double bottom,
        double radius, bool hole)
    {
        var r = Math.Min(radius, Math.Min(right - left, bottom - top) / 2);
        var size = new Size(r, r);

        if (hole)
        {
            // The same outline walked backwards, which is what makes it a hole.
            ctx.BeginFigure(new Point(left + r, top), isFilled: true);
            ctx.ArcTo(new Point(left, top + r), size, 0, false, SweepDirection.CounterClockwise);
            ctx.LineTo(new Point(left, bottom - r));
            ctx.ArcTo(new Point(left + r, bottom), size, 0, false, SweepDirection.CounterClockwise);
            ctx.LineTo(new Point(right - r, bottom));
            ctx.ArcTo(new Point(right, bottom - r), size, 0, false, SweepDirection.CounterClockwise);
            ctx.LineTo(new Point(right, top + r));
            ctx.ArcTo(new Point(right - r, top), size, 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
            return;
        }

        ctx.BeginFigure(new Point(left + r, top), isFilled: true);
        ctx.LineTo(new Point(right - r, top));
        ctx.ArcTo(new Point(right, top + r), size, 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(right, bottom - r));
        ctx.ArcTo(new Point(right - r, bottom), size, 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(left + r, bottom));
        ctx.ArcTo(new Point(left, bottom - r), size, 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(left, top + r));
        ctx.ArcTo(new Point(left + r, top), size, 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(isClosed: true);
    }
    private static void CircleAt(
        StreamGeometryContext ctx, double cx, double cy, double radius, bool hole)
    {
        var size = new Size(radius, radius);
        var sweep = hole ? SweepDirection.CounterClockwise : SweepDirection.Clockwise;

        ctx.BeginFigure(new Point(cx - radius, cy), isFilled: true);
        ctx.ArcTo(new Point(cx + radius, cy), size, 0, false, sweep);
        ctx.ArcTo(new Point(cx - radius, cy), size, 0, false, sweep);
        ctx.EndFigure(isClosed: true);
    }
    private static void Triangle(StreamGeometryContext ctx, Point a, Point b, Point c)
    {
        // Positive cross product is clockwise on a y-down canvas.
        var turn = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        ctx.BeginFigure(a, isFilled: true);

        if (turn >= 0)
        {
            ctx.LineTo(b);
            ctx.LineTo(c);
        }
        else
        {
            ctx.LineTo(c);
            ctx.LineTo(b);
        }

        ctx.EndFigure(isClosed: true);
    }
    private static Point PolarAt(double cx, double cy, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
    }
    private static StreamGeometry BuildResetZones()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            RoundedFrame(ctx, 2.4, 5.2, 21.6, 18.8, radius: 2.6, thickness: 1.4);

            // Divider well left, so the arrow gets the larger pane.
            Bar(ctx, 8.0, 5.2, 9.3, 18.8);

            ResetArrow(ctx, 15.4, 12.0, 8.6);
        }

        return geometry;
    }
    private static StreamGeometry BuildResetKeys()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            RoundedFrame(ctx, 3.4, 4.0, 20.6, 20.0, radius: 3.6, thickness: 1.3);

            ResetArrow(ctx, 12.0, 12.0, 10.6);
        }

        return geometry;
    }

    private static void Bar(
        StreamGeometryContext ctx, double left, double top, double right, double bottom)
    {
        ctx.BeginFigure(new Point(left, top), isFilled: true);
        ctx.LineTo(new Point(right, top));
        ctx.LineTo(new Point(right, bottom));
        ctx.LineTo(new Point(left, bottom));
        ctx.EndFigure(isClosed: true);
    }

    /// <summary>
    /// Wound the opposite way from everything around it, which under NonZero is
    /// how a hole is asked for.
    /// </summary>
    private static void Circle(StreamGeometryContext ctx, double radius)
    {
        ctx.BeginFigure(new Point(Center - radius, Center), isFilled: true);

        ctx.ArcTo(new Point(Center + radius, Center), new Size(radius, radius),
            0, isLargeArc: false, SweepDirection.CounterClockwise);

        ctx.ArcTo(new Point(Center - radius, Center), new Size(radius, radius),
            0, isLargeArc: false, SweepDirection.CounterClockwise);

        ctx.EndFigure(isClosed: true);
    }

    private static Point Polar(double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(Center + radius * Math.Cos(radians), Center + radius * Math.Sin(radians));
    }
}
