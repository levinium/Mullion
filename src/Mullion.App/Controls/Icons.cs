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

    /// <summary>A heart, for the one place the app asks for anything.</summary>
    public static StreamGeometry Heart { get; } = BuildHeart();

    /// <summary>
    /// A small zone inside a larger one with an arrow reaching out to its edge,
    /// for the second press that widens a window past the zone it is in.
    /// </summary>
    public static StreamGeometry Expand { get; } = BuildExpand();

    /// <summary>
    /// A window on its way into a zone, for the drag gesture that puts it there.
    /// </summary>
    public static StreamGeometry DragToZone { get; } = BuildDragToZone();

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

    /// <summary>
    /// A pencil lying at 45 degrees.
    /// <para>
    /// Built from an axis, a half-width and a set of segments along it, rather
    /// than from typed-in corners. Drawn the second way it came out thin enough
    /// to read as a line at 24px, and widening it meant moving every corner by
    /// hand while keeping the point and the ferrule square to a diagonal.
    /// </para>
    /// <para>
    /// The detail is in the gaps, not in outlines. This is one flat fill, so
    /// the only way to show that a pencil has a sharpened point and a ferrule
    /// is to leave the ground showing between them - a solid bar of the same
    /// width throughout is a crayon.
    /// </para>
    /// </summary>
    private static StreamGeometry BuildPencil()
    {
        var geometry = new StreamGeometry();

        // Corner to corner, and the barrel's half-width across it. Far thicker
        // than a pencil really is, because the shape has to survive being 21
        // pixels long: at a true proportion the barrel is two pixels wide and
        // the whole thing reads as a stroke of ink.
        var tip = new Point(4.3, 19.7);
        var end = new Point(19.7, 4.3);
        const double HalfWidth = 2.55;

        // Wide enough to be seen at 24px, which means a whole pixel. Anything
        // finer closes up under antialiasing and the parts merge back into one
        // bar - which is what "thicker" cost the first time.
        const double Gap = 1.1;

        // Better than a quarter of the whole pencil, which is roughly what a
        // sharpened one looks like. A short nib on a long barrel is a marker.
        const double PointLength = 6.2;
        const double FerruleLength = 3.6;

        var span = new Point(end.X - tip.X, end.Y - tip.Y);
        var length = Math.Sqrt(span.X * span.X + span.Y * span.Y);
        var ux = span.X / length;
        var uy = span.Y / length;

        // Across the barrel, not along it.
        var nx = -uy;
        var ny = ux;

        Point At(double along, double across) => new(
            tip.X + ux * along + nx * across,
            tip.Y + uy * along + ny * across);

        var barrelFrom = PointLength + Gap;
        var barrelTo = length - FerruleLength - Gap;

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: these glyphs are built from
            // overlapping pieces, and under EvenOdd every overlap cancels to a
            // hole - which is why an arrowhead had a bite out of it exactly
            // where it met its own shaft. A hole is asked for by winding it the
            // other way round instead.
            ctx.SetFillRule(FillRule.NonZero);

            // The sharpened cone, running from the point out to the full width
            // of the barrel. It is the whole of what makes this a pencil rather
            // than a ruler, so it gets the room to say so.
            Triangle(ctx,
                At(0, 0),
                At(PointLength, HalfWidth),
                At(PointLength, -HalfWidth));

            // The graphite, as a hole rather than a second color: an icon is
            // one flat fill, so the only dark available is the ground behind
            // it. What is left around the hole is the wood, and the cone still
            // comes to a solid point because its two edges meet before the
            // hole begins.
            // As near the point as the wood around it can survive: the hole's
            // own apex sits where the cone has narrowed to the rim's width, so
            // a thinner rim starts it closer in. Thinner than the gaps
            // elsewhere in the glyph for that reason alone.
            var rim = 0.62;

            // Short, and at the front. Graphite is the last few millimetres of
            // a pencil; run the hole down the cone and the tip stops reading as
            // sharpened and starts reading as an outlined triangle.
            var graphiteFrom = rim * PointLength / HalfWidth;
            var graphiteTo = graphiteFrom + 2.0;
            var graphiteHalf = HalfWidth * graphiteTo / PointLength - rim;

            TriangleHole(ctx,
                At(graphiteFrom, 0),
                At(graphiteTo, graphiteHalf),
                At(graphiteTo, -graphiteHalf));

            Polygon(ctx,
                At(barrelFrom, HalfWidth),
                At(barrelTo, HalfWidth),
                At(barrelTo, -HalfWidth),
                At(barrelFrom, -HalfWidth));

            // A shade wider than the barrel, the way a ferrule is: the step is
            // what says this end is metal and the other end writes.
            Polygon(ctx,
                At(length - FerruleLength, HalfWidth * 1.06),
                At(length, HalfWidth * 1.06),
                At(length, -HalfWidth * 1.06),
                At(length - FerruleLength, -HalfWidth * 1.06));
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

            // Set slightly right of center: a triangle balances on its area, not
            // its bounding box, and centered by the box it looks to be leaning back.
            ctx.BeginFigure(new Point(7.8, 5.0), isFilled: true);
            ctx.LineTo(new Point(18.4, 12.0));
            ctx.LineTo(new Point(7.8, 19.0));
            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>
    /// The cycling behavior, drawn: a window sitting in its zone, and the same
    /// key pressed again pushing it out to the edge of the larger one.
    /// <para>
    /// The window is solid and the zone an outline. Drawn as two outlines the
    /// inner square came out as a 3px ring at the size this is actually shown
    /// at, which reads as a smudge rather than as anything; solid, the two
    /// shapes are told apart by weight instead of by a detail too small to
    /// resolve, and "the filled thing is the window" is the reading anyway.
    /// </para>
    /// </summary>
    /// <summary>
    /// A double-headed arrow lying across, for the axis a zone's subzones lie
    /// along. The upright form is this one turned ninety degrees by the view -
    /// see the note below.
    /// </summary>
    public static StreamGeometry ArrowsLeftRight { get; } = BuildDoubleArrow();

    /// <summary>
    /// An arrow with a head at each end.
    /// <para>
    /// Two heads rather than one because this is not a direction to move in, it
    /// is an axis to lie along - a single head would read as "push it that way".
    /// The shaft overlaps both heads rather than butting against them: a seam
    /// avoided by a tenth of a pixel at one size comes back at another.
    /// </para>
    /// <para>
    /// Only the horizontal form exists. Drawing the upright one as a second
    /// geometry is the obvious thing and it does not work: the two have different
    /// bounding boxes - one wide and short, one tall and narrow - and every way
    /// of fitting a geometry into a box works from those bounds, so the pair
    /// never quite shares a centre and the mark hops as it swaps. Rotating one
    /// geometry about its middle makes a common centre a fact of the drawing
    /// rather than something to be tuned.
    /// </para>
    /// </summary>
    private static StreamGeometry BuildDoubleArrow()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            const double Tip = 2.6;      // how close a head comes to the edge
            const double Base = 8.4;     // where the heads meet the shaft
            const double Half = 4.2;     // half the head's width
            const double Thin = 1.15;    // half the shaft's thickness

            const double Mid = 12.0;
            const double Far = 24.0 - Tip;
            const double FarBase = 24.0 - Base;

            Bar(ctx, Base - 1.4, Mid - Thin, FarBase + 1.4, Mid + Thin);

            Triangle(ctx,
                new Point(Tip, Mid),
                new Point(Base, Mid - Half),
                new Point(Base, Mid + Half));

            Triangle(ctx,
                new Point(Far, Mid),
                new Point(FarBase, Mid + Half),
                new Point(FarBase, Mid - Half));
        }

        return geometry;
    }

    private static StreamGeometry BuildExpand()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            // The larger zone. Short of the full canvas height so the icon sits
            // on the same optical line as the text it labels.
            RoundedFrame(ctx, 2.0, 5.0, 22.0, 19.0, radius: 2.4, thickness: 1.3);

            // The window as it sits now: square, and left of center so there is
            // room for the arrow to travel.
            RoundedRect(ctx, 4.6, 9.2, 10.2, 14.8, radius: 1.0, hole: false);

            // Overlapping the head rather than butted against it - a seam
            // avoided by a tenth of a pixel comes back at some other size.
            Bar(ctx, 11.6, 11.15, 17.6, 12.85);

            // Stopping short of the frame's inner edge at 20.6. Run right up to
            // it and the head merges with the wall it is pointing at, which is
            // the one relationship the icon exists to show.
            Triangle(ctx,
                new Point(19.8, 12.0),
                new Point(16.6, 9.2),
                new Point(16.6, 14.8));
        }

        return geometry;
    }

    /// <summary>
    /// A heart.
    /// <para>
    /// Two overlapping circles for the lobes and a triangle for the point.
    /// Walked as a single outline instead it came out as a V wedged between two
    /// lumps: the cleft is not a point you place, it is wherever the lobes
    /// happen to cross, and choosing it by hand puts it where the curves do not
    /// agree with.
    /// </para>
    /// <para>
    /// The sides run from the point TANGENT to each lobe, which is the whole
    /// difference between this and a heart in a corset. Take the triangle up to
    /// the lobes' widest point instead and its edges are chords: the circle
    /// bulges out past the straight line and comes back to meet it lower down,
    /// so the silhouette swells and pinches. A tangent leaves the circle
    /// without changing direction, and there is nothing to pinch.
    /// </para>
    /// </summary>
    private static StreamGeometry BuildHeart()
    {
        var geometry = new StreamGeometry();

        const double LobeY = 8.9;
        const double LobeR = 4.5;

        // Less than the radius, so the lobes overlap rather than merely touch.
        // Touching, they meet on their centre line and the notch cuts down to
        // it; overlapping, they cross well above it, which is where a heart's
        // notch sits.
        const double Spread = 3.8;

        const double Tip = 20.1;

        var apex = new Point(12, Tip);

        // Where a line from the point grazes a lobe. Rotating the direction to
        // the centre by asin(r/d) turns it into the tangent's direction; the
        // sign picks which side of the lobe it grazes, and each lobe wants its
        // outer one.
        Point GrazeOf(double centreX, double outward)
        {
            var vx = centreX - apex.X;
            var vy = LobeY - apex.Y;
            var distance = Math.Sqrt(vx * vx + vy * vy);
            var along = Math.Sqrt(distance * distance - LobeR * LobeR);
            var turn = Math.Asin(LobeR / distance) * outward;

            var cos = Math.Cos(turn);
            var sin = Math.Sin(turn);

            return new Point(
                apex.X + (vx * cos - vy * sin) / distance * along,
                apex.Y + (vx * sin + vy * cos) / distance * along);
        }

        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            CircleAt(ctx, 12 - Spread, LobeY, LobeR, hole: false);
            CircleAt(ctx, 12 + Spread, LobeY, LobeR, hole: false);

            Triangle(ctx, apex, GrazeOf(12 - Spread, -1), GrazeOf(12 + Spread, 1));

            // Over the point where the two lobes cross, low on the centre line.
            // Three boundaries meet there - both circles and, near enough, the
            // triangle - and the rasteriser left a single pixel of ground
            // showing through the middle of the heart. Everything here is well
            // inside the silhouette, so it changes nothing except that.
            Bar(ctx, 12 - Spread, LobeY, 12 + Spread, LobeY + LobeR);
        }

        return geometry;
    }

    /// <summary>
    /// The drag gesture, drawn: the pointer, over the zone it is aiming at.
    /// <para>
    /// Two shapes, not three. A window, a zone and an arrow between them is the
    /// obvious drawing and it does not survive: this is rendered at 24px, so a
    /// shaft two units long is two pixels long, and the arrow came out as a
    /// smudge between two rectangles.
    /// </para>
    /// <para>
    /// The pointer earns its place twice over - it is legible at this size, and
    /// it says "mouse" against the keyboard note sitting beside it, which is
    /// the actual difference between the two gestures.
    /// </para>
    /// </summary>
    private static StreamGeometry BuildDragToZone()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);

            // The zone, drawn to match the expand icon's outer frame so the two
            // notes are visibly about the same thing.
            RoundedFrame(ctx, 2.0, 4.0, 22.0, 20.0, radius: 2.2, thickness: 1.3);

            Pointer(ctx, x: 8.0, y: 6.4, scale: 0.72);
        }

        return geometry;
    }

    /// <summary>
    /// A mouse cursor, tip at <paramref name="x"/>,<paramref name="y"/>.
    /// </summary>
    /// <remarks>
    /// The familiar silhouette rather than a plain triangle: the notch where the
    /// tail meets the head is the whole of what makes it read as a pointer and
    /// not as an arrowhead, and it is still legible when the thing is nine
    /// pixels tall.
    /// </remarks>
    private static void Pointer(StreamGeometryContext ctx, double x, double y, double scale)
    {
        Point At(double px, double py) => new(x + px * scale, y + py * scale);

        Polygon(ctx,
            At(0.0, 0.0),
            At(0.0, 14.0),
            At(3.6, 10.6),
            At(6.0, 15.6),
            At(8.4, 14.5),
            At(5.9, 9.7),
            At(10.4, 9.3));
    }

    /// <summary>
    /// A triangle wound AGAINST the fill, so it cuts a hole in whatever it sits
    /// inside.
    /// <para>
    /// The only way to get a second tone out of a single-color icon: the hole
    /// shows the ground behind the glyph, which on a toolbar is exactly the dark
    /// the shape is asking for.
    /// </para>
    /// <para>
    /// Written as the mirror of <see cref="Triangle"/> rather than of Polygon.
    /// The two normalize to opposite directions - one tests a cross product,
    /// the other a shoelace sum - so a hole built from the wrong one is wound
    /// the same way as the shape it is cutting into, merges with it, and leaves
    /// no hole at all. Which is what it did.
    /// </para>
    /// </summary>
    private static void TriangleHole(StreamGeometryContext ctx, Point a, Point b, Point c)
    {
        var turn = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        ctx.BeginFigure(a, isFilled: true);

        // Triangle walks b then c when the turn is positive; this walks the
        // other way round for the same input.
        if (turn >= 0)
        {
            ctx.LineTo(c);
            ctx.LineTo(b);
        }
        else
        {
            ctx.LineTo(b);
            ctx.LineTo(c);
        }

        ctx.EndFigure(isClosed: true);
    }

    /// <summary>
    /// A closed shape, wound so it fills rather than cancels.
    /// <para>
    /// The same rule <see cref="Triangle"/> follows, for the shapes that need
    /// more than three corners: under NonZero a figure wound against its
    /// neighbours punches a hole in them instead of joining them.
    /// </para>
    /// </summary>
    private static void Polygon(StreamGeometryContext ctx, params Point[] points)
    {
        // Shoelace: positive area is clockwise on a y-down canvas.
        var area = 0.0;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += (b.X - a.X) * (b.Y + a.Y);
        }

        ctx.BeginFigure(points[0], isFilled: true);

        if (area >= 0)
        {
            for (var i = 1; i < points.Length; i++) ctx.LineTo(points[i]);
        }
        else
        {
            for (var i = points.Length - 1; i >= 1; i--) ctx.LineTo(points[i]);
        }

        ctx.EndFigure(isClosed: true);
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
