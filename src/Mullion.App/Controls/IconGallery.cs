using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;

// System.IO.Path is in scope through implicit usings, so the shape needs saying.
using Shape = Avalonia.Controls.Shapes.Shape;
using Path = Avalonia.Controls.Shapes.Path;

namespace Mullion.App.Controls;

/// <summary>
/// Every icon, at the size it ships at and blown up, in each of the states the
/// theme gives it. Opened with --icons.
/// <para>
/// Built because choosing an icon by looking at it inside the app is slow and
/// partial: you see one state of one glyph at a time, and the question is always
/// how it sits next to the others. A glyph that reads perfectly at 96px can be a
/// smudge at 24, and the only way to know which is which is to put them side by
/// side at both sizes.
/// </para>
/// </summary>
public static class IconGallery
{
    private const double Big = 2.6;

    public static Window Create() => new()
    {
        Title = "Mullion icons",
        Width = 760,
        Height = 1370,
        Content = new ScrollViewer { Content = Body(), Padding = new Thickness(20) },
    };

    private static Control Body()
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(Heading("In use"));
        panel.Children.Add(Row("Gear — settings", Icons.Gear));
        panel.Children.Add(Row("Pencil — edit zones", Icons.Pencil));
        panel.Children.Add(Row("Check — keep changes", Icons.Check));
        panel.Children.Add(Row("Cross — discard changes", Icons.Cross));
        panel.Children.Add(Row("Undo", Icons.Undo));
        panel.Children.Add(Row("Redo", Icons.Redo));
        panel.Children.Add(Row("Refresh — rescan", Icons.Refresh));
        panel.Children.Add(Row("Pause — hotkeys", Icons.Pause));
        panel.Children.Add(Row("Play — resume", Icons.Play));

        panel.Children.Add(Heading("Snap — pick one"));
        panel.Children.Add(Row("A  magnet with poles", IconCandidates.MagnetPoles));
        panel.Children.Add(Row("B  magnet, plain legs", IconCandidates.MagnetPlain));
        panel.Children.Add(Row("C  walls, arrow between  |<->|", IconCandidates.SnapBetween));
        panel.Children.Add(Row("D  walls, arrows outward", IconCandidates.SnapToEdges));

        panel.Children.Add(Heading("Reset zones — pick one"));
        panel.Children.Add(Row("E  panes", IconCandidates.Zones));
        panel.Children.Add(Row("F  panes with reset arrow", IconCandidates.ZonesReset));

        panel.Children.Add(Heading("Reset keys — pick one"));
        panel.Children.Add(Row("G  keycap", IconCandidates.Keycap));
        panel.Children.Add(Row("H  keycap with reset arrow", IconCandidates.KeycapReset));

        return panel;
    }

    private static Control Heading(string text) => new TextBlock
    {
        Text = text,
        Margin = new Thickness(0, 12, 0, 4),
        FontWeight = FontWeight.SemiBold,
        FontSize = 15,
    };

    /// <summary>
    /// One icon across every state it can be in, then blown up. The states are
    /// what the question actually turns on: an icon can be legible at rest and
    /// vanish when it is the faint disabled one.
    /// </summary>
    private static Control Row(string name, StreamGeometry geometry)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 1),
        };

        row.Children.Add(Small(geometry, "InkDim"));
        row.Children.Add(Small(geometry, "Ink"));
        row.Children.Add(Small(geometry, "InkFaint"));
        row.Children.Add(Small(geometry, "Accent"));
        row.Children.Add(Large(geometry));

        row.Children.Add(new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });

        return row;
    }

    private static Control Small(StreamGeometry geometry, string brush) => new Border
    {
        Width = 30,
        Height = 30,
        Child = new Path
        {
            Data = geometry,
            Width = Icons.Canvas,
            Height = Icons.Canvas,
            Stretch = Stretch.None,
            [!Shape.FillProperty] = new DynamicResourceExtension(brush),
        },
    };

    private static Control Large(StreamGeometry geometry) => new Border
    {
        Width = Icons.Canvas * Big,
        Height = Icons.Canvas * Big,
        Child = new Path
        {
            Data = geometry,
            Width = Icons.Canvas,
            Height = Icons.Canvas,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,

            // Scaled rather than stretched, so the blown-up version is the same
            // drawing as the small one and not a differently proportioned copy.
            RenderTransform = new ScaleTransform(Big, Big),
            RenderTransformOrigin = RelativePoint.TopLeft,
            [!Shape.FillProperty] = new DynamicResourceExtension("InkDim"),
        },
    };
}
