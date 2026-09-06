namespace Mullion.Core.Geometry;

/// <summary>
/// A fractional rectangle in 0..1 space, relative to a display's work area.
/// Zones are stored this way rather than in pixels so they survive resolution
/// changes, DPI changes and the taskbar moving or resizing.
/// </summary>
public readonly record struct NormRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;

    public static readonly NormRect Full = new(0, 0, 1, 1);

    public static NormRect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>
    /// Project onto a physical work area.
    /// </summary>
    /// <remarks>
    /// Rounds the four EDGES independently rather than rounding position and size.
    /// This is what guarantees adjacent zones share an exact integer boundary
    /// (<c>a.Right == b.Left</c> bit-for-bit). Rounding position-and-size instead
    /// leaves a one-pixel seam or overlap on most boundaries, which is visible on
    /// every snap and is the single easiest way to make tiling look broken.
    /// </remarks>
    public PxRect Project(PxRect work)
    {
        var left = work.Left + (int)Math.Round(X * work.Width, MidpointRounding.AwayFromZero);
        var top = work.Top + (int)Math.Round(Y * work.Height, MidpointRounding.AwayFromZero);
        var right = work.Left + (int)Math.Round(Right * work.Width, MidpointRounding.AwayFromZero);
        var bottom = work.Top + (int)Math.Round(Bottom * work.Height, MidpointRounding.AwayFromZero);
        return PxRect.FromLtrb(left, top, right, bottom);
    }

    /// <summary>Smallest NormRect containing both. Backs the union-key rule.</summary>
    public NormRect Union(NormRect other) => FromEdges(
        Math.Min(X, other.X),
        Math.Min(Y, other.Y),
        Math.Max(Right, other.Right),
        Math.Max(Bottom, other.Bottom));

    public static NormRect Union(IEnumerable<NormRect> rects)
    {
        NormRect? acc = null;
        foreach (var r in rects) acc = acc is null ? r : acc.Value.Union(r);
        return acc ?? Full;
    }

    /// <summary>Subdivide along an axis, splitting THIS rect by normalized weights.</summary>
    public IReadOnlyList<NormRect> Split(Axis axis, IReadOnlyList<double> weights)
    {
        ArgumentOutOfRangeException.ThrowIfZero(weights.Count);

        var total = weights.Sum();
        if (total <= 0) throw new ArgumentException("Weights must sum to a positive value.", nameof(weights));

        var result = new List<NormRect>(weights.Count);
        var acc = 0.0;
        foreach (var w in weights)
        {
            var from = acc / total;
            acc += w;
            var to = acc / total;

            result.Add(axis == Axis.Horizontal
                ? FromEdges(X + from * W, Y, X + to * W, Bottom)
                : FromEdges(X, Y + from * H, Right, Y + to * H));
        }

        return result;
    }

    public override string ToString() =>
        $"({X:0.####},{Y:0.####} {W:0.####}x{H:0.####})";
}

public enum Axis
{
    Horizontal,
    Vertical,
}
