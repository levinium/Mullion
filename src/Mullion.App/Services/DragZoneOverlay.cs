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

    private readonly List<Window> _windows = [];

    private IReadOnlyList<DropTarget> _shown = [];
    private DropTarget? _hovered;

    /// <summary>Draw these targets, highlighting <paramref name="hovered"/>.</summary>
    public void Show(IReadOnlyList<DropTarget> targets, DropTarget? hovered)
    {
        Dispatcher.UIThread.Post(() => Render(targets, hovered));
    }

    public void Hide() => Dispatcher.UIThread.Post(Clear);

    private void Render(IReadOnlyList<DropTarget> targets, DropTarget? hovered)
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
                var window = CreateWindow();
                window.Position = new PixelPoint(target.Bounds.X, target.Bounds.Y);
                window.Width = target.Bounds.Width;
                window.Height = target.Bounds.Height;
                window.Show();
                _windows.Add(window);
            }
        }

        if (ReferenceEquals(_hovered, hovered) && _windows.Count == _shown.Count) return;

        _hovered = hovered;

        for (var i = 0; i < _windows.Count && i < _shown.Count; i++)
        {
            if (_windows[i].Content is not Border border) continue;

            var isHovered = hovered is not null && ReferenceEquals(_shown[i], hovered);

            border.Background = new SolidColorBrush(Accent, isHovered ? 0.28 : 0.10);
            border.BorderThickness = new Thickness(isHovered ? 3 : 1);
        }
    }

    private void Clear()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        _shown = [];
        _hovered = null;
    }

    private static Window CreateWindow() => new()
    {
        WindowDecorations = WindowDecorations.None,
        Background = Brushes.Transparent,
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        ShowInTaskbar = false,
        Topmost = true,
        CanResize = false,
        ShowActivated = false,
        IsHitTestVisible = false,

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Accent, 0.10),
        },
    };

    public void Dispose() => Clear();
}
