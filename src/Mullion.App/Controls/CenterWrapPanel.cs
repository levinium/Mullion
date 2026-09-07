using Avalonia;
using Avalonia.Controls;

namespace Mullion.App.Controls;

/// <summary>
/// A WrapPanel that centers each line rather than pushing every line left.
/// <para>
/// Used for the key chips, where the runs of a chord are separate elements so a
/// long chord can break between them instead of mid-word. A plain WrapPanel
/// aligns each line to the left edge, so "Ctrl+ / Shift+ / Q" comes out ragged -
/// the widest run sets the chip's width and the shorter ones hang off it. There
/// is no line-alignment property to set: WrapPanel arranges each line from the
/// left and offers no say in it, so centering means owning the arrange pass.
/// </para>
/// </summary>
public sealed class CenterWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));

        double width = 0, height = 0;

        foreach (var line in Lines(availableSize.Width))
        {
            width = Math.Max(width, line.Width);
            height += line.Height;
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;

        foreach (var line in Lines(finalSize.Width))
        {
            // The whole point: the line's own leftover space, split evenly.
            var x = Math.Max(0, (finalSize.Width - line.Width) / 2);

            for (var i = line.First; i < line.First + line.Count; i++)
            {
                var child = Children[i];
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, line.Height));
                x += child.DesiredSize.Width;
            }

            y += line.Height;
        }

        return finalSize;
    }

    private readonly record struct Line(int First, int Count, double Width, double Height);

    /// <summary>
    /// Groups the children into lines at a given width.
    /// <para>
    /// Measure and arrange must break at exactly the same places or a line's
    /// centering is computed against a width it does not have, so both go
    /// through here rather than each keeping its own running totals.
    /// </para>
    /// </summary>
    private IEnumerable<Line> Lines(double available)
    {
        var first = 0;
        double width = 0, height = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            var size = Children[i].DesiredSize;

            // A run wider than the panel still gets a line of its own - there is
            // nowhere else for it to go, and the Viewbox above scales it.
            if (i > first && width + size.Width > available)
            {
                yield return new Line(first, i - first, width, height);
                first = i;
                width = 0;
                height = 0;
            }

            width += size.Width;
            height = Math.Max(height, size.Height);
        }

        if (Children.Count > first)
            yield return new Line(first, Children.Count - first, width, height);
    }
}
