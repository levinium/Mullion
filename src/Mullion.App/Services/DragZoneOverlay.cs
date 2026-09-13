using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Mullion.Core.Geometry;
using Mullion.Core.Layout;

namespace Mullion.App.Services;

/// <summary>
/// Shows where a dragged window would land, and which zone it is over.
/// <para>
/// One window per zone rather than a single window covering the desk. A window
/// spanning several monitors gets one scaling factor for all of them, so on a
/// mixed-DPI desk everything drawn on the lower-scaled monitor lands in the
/// wrong place; and a full-desk window on Windows is clamped to the monitor it
/// is considered to be on. Per-zone windows are each on exactly one display and
/// sidestep both.
/// </para>
/// <para>
/// Click-through and never activated, like <see cref="ZoneFlashOverlay"/>: an
/// overlay that took focus would end the drag it exists to assist.
/// </para>
/// </summary>
public sealed class DragZoneOverlay : IDisposable
{
    private static readonly Color Accent = Color.FromRgb(0x4C, 0x8B, 0xF5);

    /// <summary>A zone's overlay window and the two rectangles drawn in it.</summary>
    private sealed record Layer(Window Window, Border Zone, Border Preview);

    /// <summary>How one zone should be drawn right now.</summary>
    /// <param name="ZoneOpacity">Alpha of the zone's own fill.</param>
    /// <param name="BorderThickness">Thickness of its outline.</param>
    /// <param name="Preview">
    /// Where the window will land inside this zone, as fractions of it, or null
    /// when the whole zone is the answer and its own fill says so.
    /// </param>
    public readonly record struct ZoneAppearance(
        double ZoneOpacity, double BorderThickness, NormRect? Preview);

    /// <summary>
    /// The whole drawing rule, stated once: what a zone looks like given whether
    /// the pointer is over it and where the window would actually end up.
    /// <para>
    /// Pulled out of the rendering so it can be checked without standing up
    /// windows. The case worth protecting is the third one - a zone that fills
    /// itself while also showing a smaller preview is making two contradictory
    /// promises, and it is the promise the user reads that matters.
    /// </para>
    /// </summary>
    public static ZoneAppearance AppearanceOf(bool isHovered, PxRect zone, PxRect? landing)
    {
        if (!isHovered) return new ZoneAppearance(0.10, 1, null);

        // Landing on the whole zone is what the zone's own fill already says.
        if (landing is not { } target || ZoneFit.Fills(target, zone))
            return new ZoneAppearance(0.28, 3, null);

        return new ZoneAppearance(0.0, 3, NormRect.Within(target, zone));
    }

    private readonly List<Layer> _layers = [];

    private IReadOnlyList<DropTarget> _shown = [];
    private DropTarget? _hovered;
    private PxRect? _preview;

    /// <summary>
    /// Draw these targets, highlighting <paramref name="hovered"/>.
    /// </summary>
    /// <param name="preview">
    /// Where the window will actually end up, when that is not the whole zone -
    /// dropping a window on the zone it already fills returns it to its old
    /// size rather than refilling. Filling the zone to advertise that would
    /// promise the opposite of what is about to happen.
    /// </param>
    public void Show(IReadOnlyList<DropTarget> targets, DropTarget? hovered, PxRect? preview = null)
    {
        Dispatcher.UIThread.Post(() => Render(targets, hovered, preview));
    }

    public void Hide() => Dispatcher.UIThread.Post(Clear);

    private void Render(IReadOnlyList<DropTarget> targets, DropTarget? hovered, PxRect? preview)
    {
        // Rebuild only when the set changes. A drag raises location changes
        // continuously, and tearing down windows on every one of them flickers
        // and is far too slow to keep up with the pointer.
        if (!ReferenceEquals(_shown, targets))
        {
            Clear();
            _shown = targets;

            foreach (var target in targets)
            {
                var layer = CreateLayer();
                OverlayPlacement.PlaceAt(layer.Window, target.Bounds);
                layer.Window.Show();
                _layers.Add(layer);
            }
        }

        if (ReferenceEquals(_hovered, hovered) && _preview == preview && _layers.Count == _shown.Count)
            return;

        _hovered = hovered;
        _preview = preview;

        for (var i = 0; i < _layers.Count && i < _shown.Count; i++)
        {
            var layer = _layers[i];
            var isHovered = hovered is not null && ReferenceEquals(_shown[i], hovered);
            var look = AppearanceOf(isHovered, _shown[i].Bounds, preview);

            layer.Zone.Background = new SolidColorBrush(Accent, look.ZoneOpacity);
            layer.Zone.BorderThickness = new Thickness(look.BorderThickness);

            layer.Preview.IsVisible = look.Preview is not null;

            if (look.Preview is { } where) Place(layer, where);
        }
    }

    /// <summary>Lay the preview out inside its zone's window.</summary>
    private static void Place(Layer layer, NormRect where)
    {
        var width = layer.Window.Width;
        var height = layer.Window.Height;

        Canvas.SetLeft(layer.Preview, where.X * width);
        Canvas.SetTop(layer.Preview, where.Y * height);

        layer.Preview.Width = where.W * width;
        layer.Preview.Height = where.H * height;
    }

    private void Clear()
    {
        foreach (var layer in _layers) layer.Window.Close();
        _layers.Clear();
        _shown = [];
        _hovered = null;
        _preview = null;
    }

    private static Layer CreateLayer()
    {
        var zone = new Border
        {
            BorderBrush = new SolidColorBrush(Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Accent, 0.10),
        };

        var preview = new Border
        {
            BorderBrush = new SolidColorBrush(Accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Accent, 0.28),
            IsVisible = false,
        };

        // The preview sits in a Canvas so it can be placed at an arbitrary
        // offset inside the zone; the zone itself stretches behind it.
        var window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            ShowInTaskbar = false,
            Topmost = true,
            CanResize = false,
            ShowActivated = false,
            IsHitTestVisible = false,

            Content = new Grid { Children = { zone, new Canvas { Children = { preview } } } },
        };

        return new Layer(window, zone, preview);
    }

    public void Dispose() => Clear();
}
