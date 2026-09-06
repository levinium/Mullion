namespace Mullion.Core.Geometry;

/// <summary>
/// An integer rectangle in physical pixels, in virtual-screen coordinates.
/// X and Y may be negative: a monitor placed left of or above the primary
/// display has negative origin in Windows' virtual desktop space.
/// </summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public long Area => (long)Width * Height;
    public int LongAxis => Math.Max(Width, Height);
    public int ShortAxis => Math.Min(Width, Height);

    /// <summary>Long-axis over short-axis. Identical whether the panel is rotated or not.</summary>
    public double Elongation => ShortAxis == 0 ? 0 : (double)LongAxis / ShortAxis;

    /// <summary>Width over height. Unlike <see cref="Elongation"/> this changes under rotation.</summary>
    public double Aspect => Height == 0 ? 0 : (double)Width / Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PxRect FromLtrb(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public PxRect Deflate(int left, int top, int right, int bottom) =>
        FromLtrb(Left + left, Top + top, Right - right, Bottom - bottom);

    /// <summary>Pixels of vertical overlap with <paramref name="other"/>; 0 if disjoint.</summary>
    public int VerticalOverlap(PxRect other) =>
        Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top));

    /// <summary>Pixels of horizontal overlap with <paramref name="other"/>; 0 if disjoint.</summary>
    public int HorizontalOverlap(PxRect other) =>
        Math.Max(0, Math.Min(Right, other.Right) - Math.Max(Left, other.Left));

    public bool Intersects(PxRect other) =>
        VerticalOverlap(other) > 0 && HorizontalOverlap(other) > 0;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>Smallest rectangle containing both. Used for zone unions and virtual bounds.</summary>
    public PxRect Union(PxRect other) => FromLtrb(
        Math.Min(Left, other.Left),
        Math.Min(Top, other.Top),
        Math.Max(Right, other.Right),
        Math.Max(Bottom, other.Bottom));

    public static PxRect Union(IEnumerable<PxRect> rects)
    {
        PxRect? acc = null;
        foreach (var r in rects) acc = acc is null ? r : acc.Value.Union(r);
        return acc ?? default;
    }

    public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
}
