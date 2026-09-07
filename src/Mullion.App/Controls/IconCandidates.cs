using Avalonia;
using Avalonia.Media;

namespace Mullion.App.Controls;

/// <summary>
/// Icons under review, drawn on the same 24x24 canvas as <see cref="Icons"/>.
/// <para>
/// Separate from the set in use so that choosing between them is a matter of
/// deleting the losers rather than untangling them. Shown side by side by the
/// hidden --icons window: a glyph that reads perfectly at 96px can be a smudge
/// at 24, and the only way to know which is which is to look at both.
/// </para>
/// </summary>
public static class IconCandidates
{
    private const double Center = 12.0;

    // ---- Snap ---------------------------------------------------------------

    /// <summary>A horseshoe magnet with its poles shown as banded tips.</summary>
    public static StreamGeometry MagnetPoles { get; } = BuildMagnet(poles: true);

    /// <summary>The same horseshoe with plain legs, for comparison.</summary>
    public static StreamGeometry MagnetPlain { get; } = BuildMagnet(poles: false);

    /// <summary>Two walls with a double-headed arrow between them: |&lt;-&gt;|.</summary>
    public static StreamGeometry SnapBetween { get; } = BuildSnapBetween();

    /// <summary>Two walls with arrows pointing OUT to them, which is what snapping does.</summary>
    public static StreamGeometry SnapToEdges { get; } = BuildSnapToEdges();

    // ---- Reset zones --------------------------------------------------------

    /// <summary>Three panes of a split display.</summary>
    public static StreamGeometry Zones { get; } = BuildZones(withReset: false);

    /// <summary>The same, with a reset arrow curling over the corner.</summary>
    public static StreamGeometry ZonesReset { get; } = BuildZones(withReset: true);

    // ---- Reset keys ---------------------------------------------------------

    /// <summary>A single keycap.</summary>
    public static StreamGeometry Keycap { get; } = BuildKeycap(withReset: false);

    /// <summary>A keycap with the same reset arrow, so the pair reads as a set.</summary>
    public static StreamGeometry KeycapReset { get; } = BuildKeycap(withReset: true);

    private static StreamGeometry BuildMagnet(bool poles)
    {
        const double Outer = 7.0;
        const double Inner = 3.8;
        const double Top = 11.4;
        const double Shoulder = 14.6;
        const double Foot = 19.4;

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: overlapping pieces have to
            // union rather than cancel each other into holes. Anything that IS a
            // hole is wound the other way round instead - see RectReversed.
            ctx.SetFillRule(FillRule.NonZero);

            // The horseshoe: round over the top, legs cut square at the bottom.
            // With poles it stops short, and the tips are drawn as their own
            // blocks so there is a visible seam across each leg - which is the
            // whole of what makes it a magnet rather than an archway.
            var legEnd = poles ? Shoulder : Foot;

            ctx.BeginFigure(new Point(Center - Outer, Top), isFilled: true);

            ctx.ArcTo(new Point(Center + Outer, Top), new Size(Outer, Outer),
                0, isLargeArc: false, SweepDirection.Clockwise);

            ctx.LineTo(new Point(Center + Outer, legEnd));
            ctx.LineTo(new Point(Center + Inner, legEnd));
            ctx.LineTo(new Point(Center + Inner, Top));

            ctx.ArcTo(new Point(Center - Inner, Top), new Size(Inner, Inner),
                0, isLargeArc: false, SweepDirection.CounterClockwise);

            ctx.LineTo(new Point(Center - Inner, legEnd));
            ctx.LineTo(new Point(Center - Outer, legEnd));

            ctx.EndFigure(isClosed: true);

            if (!poles) return geometry;

            // Wider than the legs as well as separated from them, so the tips
            // read as banded poles rather than as legs with a nick in them.
            Rect(ctx, Center - Outer - 0.8, Shoulder + 1.6, Center - Inner + 0.8, Foot);
            Rect(ctx, Center + Inner - 0.8, Shoulder + 1.6, Center + Outer + 0.8, Foot);
        }

        return geometry;
    }

    /// <summary>
    /// Two walls with a double-headed arrow between them. Says "this distance
    /// is being measured" more than "this edge is being snapped to", but it is
    /// the shape asked for and it reads cleanly at 24px.
    /// </summary>
    private static StreamGeometry BuildSnapBetween()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: overlapping pieces have to
            // union rather than cancel each other into holes. Anything that IS a
            // hole is wound the other way round instead - see RectReversed.
            ctx.SetFillRule(FillRule.NonZero);

            Rect(ctx, 3.4, 5.0, 5.4, 19.0);
            Rect(ctx, 18.6, 5.0, 20.6, 19.0);

            // Shaft, with a head at each end.
            Rect(ctx, 9.4, 11.0, 14.6, 13.0);

            Triangle(ctx, new Point(6.6, 12.0), new Point(10.4, 8.8), new Point(10.4, 15.2));
            Triangle(ctx, new Point(17.4, 12.0), new Point(13.6, 8.8), new Point(13.6, 15.2));
        }

        return geometry;
    }

    /// <summary>
    /// Two walls with a pair of arrows pointing outward INTO them - a thing
    /// being pushed onto its stops, which is what a snap actually does.
    /// </summary>
    private static StreamGeometry BuildSnapToEdges()
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: overlapping pieces have to
            // union rather than cancel each other into holes. Anything that IS a
            // hole is wound the other way round instead - see RectReversed.
            ctx.SetFillRule(FillRule.NonZero);

            Rect(ctx, 3.4, 5.0, 5.4, 19.0);
            Rect(ctx, 18.6, 5.0, 20.6, 19.0);

            Rect(ctx, 10.6, 11.0, 13.4, 13.0);

            Triangle(ctx, new Point(6.4, 12.0), new Point(10.2, 8.8), new Point(10.2, 15.2));
            Triangle(ctx, new Point(17.6, 12.0), new Point(13.8, 8.8), new Point(13.8, 15.2));
        }

        return geometry;
    }

    /// <summary>
    /// A display split into panes. Hollow rather than solid: three filled blocks
    /// read as a bar chart, while outlines with gaps between them read as the
    /// zones the diagram is already showing.
    /// </summary>
    private static StreamGeometry BuildZones(bool withReset)
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: overlapping pieces have to
            // union rather than cancel each other into holes. Anything that IS a
            // hole is wound the other way round instead - see RectReversed.
            ctx.SetFillRule(FillRule.NonZero);

            var right = withReset ? 15.4 : 21.0;
            var bottom = withReset ? 15.4 : 18.6;

            Frame(ctx, 3.0, 5.4, right, bottom, 1.7);

            // The dividers, drawn as bars inside the frame.
            var mid = 3.0 + (right - 3.0) * 0.42;
            Rect(ctx, mid - 0.85, 5.4, mid + 0.85, bottom);

            if (!withReset) return geometry;

            ResetArrow(ctx, 17.0, 17.0, 8.6);
        }

        return geometry;
    }

    /// <summary>A keycap: a rounded-off square with a lighter centre.</summary>
    private static StreamGeometry BuildKeycap(bool withReset)
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            // NonZero, not the default EvenOdd: overlapping pieces have to
            // union rather than cancel each other into holes. Anything that IS a
            // hole is wound the other way round instead - see RectReversed.
            ctx.SetFillRule(FillRule.NonZero);

            var right = withReset ? 14.6 : 19.4;
            var bottom = withReset ? 14.6 : 19.4;

            Frame(ctx, 4.0, 4.0, right, bottom, 1.8);

            // The keycap's own face, inset unevenly - deeper at the bottom, the
            // way a key is actually moulded. An empty box reads as a checkbox
            // and a box with a dash in it reads as a minus sign.
            Frame(ctx, 6.6, 6.2, right - 2.6, bottom - 3.4, 1.4);

            if (!withReset) return geometry;

            ResetArrow(ctx, 17.0, 17.0, 8.6);
        }

        return geometry;
    }

    /// <summary>
    /// A small circular arrow, for stamping "put this back" onto another glyph.
    /// <para>
    /// Nearly a full turn rather than a half, so it is not mistaken for the undo
    /// arrow beside it in the same row - that one is an arc over the top, this
    /// one is a ring.
    /// </para>
    /// </summary>
    private static void ResetArrow(StreamGeometryContext ctx, double cx, double cy, double size)
    {
        var outer = size / 2;
        var inner = outer - 1.9;

        var start = Polar(cx, cy, outer, -60);
        var end = Polar(cx, cy, outer, 200);
        var innerStart = Polar(cx, cy, inner, 200);
        var innerEnd = Polar(cx, cy, inner, -60);

        ctx.BeginFigure(start, isFilled: true);
        ctx.ArcTo(end, new Size(outer, outer), 0, isLargeArc: true, SweepDirection.Clockwise);
        ctx.LineTo(innerStart);
        ctx.ArcTo(innerEnd, new Size(inner, inner), 0, isLargeArc: true, SweepDirection.CounterClockwise);
        ctx.EndFigure(isClosed: true);

        // The head, on the open end, pointing the way round it goes.
        var mid = (outer + inner) / 2;
        var tip = Polar(cx, cy, mid, -60);

        Triangle(ctx,
            new Point(tip.X + 2.4, tip.Y + 0.6),
            new Point(tip.X - 1.4, tip.Y + 2.2),
            new Point(tip.X - 0.9, tip.Y - 2.1));
    }

    /// <summary>An outline: a rectangle with a smaller one cut out of it.</summary>
    private static void Frame(
        StreamGeometryContext ctx, double left, double top, double right, double bottom, double thickness)
    {
        Rect(ctx, left, top, right, bottom);
        RectReversed(ctx, left + thickness, top + thickness, right - thickness, bottom - thickness);
    }

    /// <summary>The same rectangle wound the other way, which under NonZero cuts a hole.</summary>
    private static void RectReversed(
        StreamGeometryContext ctx, double left, double top, double right, double bottom)
    {
        ctx.BeginFigure(new Point(left, top), isFilled: true);
        ctx.LineTo(new Point(left, bottom));
        ctx.LineTo(new Point(right, bottom));
        ctx.LineTo(new Point(right, top));
        ctx.EndFigure(isClosed: true);
    }

    private static void Rect(
        StreamGeometryContext ctx, double left, double top, double right, double bottom)
    {
        ctx.BeginFigure(new Point(left, top), isFilled: true);
        ctx.LineTo(new Point(right, top));
        ctx.LineTo(new Point(right, bottom));
        ctx.LineTo(new Point(left, bottom));
        ctx.EndFigure(isClosed: true);
    }

    private static void Triangle(StreamGeometryContext ctx, Point a, Point b, Point c)
    {
        ctx.BeginFigure(a, isFilled: true);
        ctx.LineTo(b);
        ctx.LineTo(c);
        ctx.EndFigure(isClosed: true);
    }

    private static Point Polar(double cx, double cy, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
    }
}
