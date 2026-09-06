using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Mullion.Core.Geometry;

namespace Mullion.App.Services;

/// <summary>
/// Briefly outlines the zone a window was just sent to.
/// <para>
/// Without it a hotkey that lands on an already-correctly-sized window looks
/// like nothing happened, and one that is silently ignored looks identical to
/// one that worked. The flash makes both distinguishable at a glance.
/// </para>
/// </summary>
public sealed class ZoneFlashOverlay : IDisposable
{
    private Window? _window;
    private DispatcherTimer? _timer;

    public void Flash(PxRect target, TimeSpan? duration = null)
    {
        Dispatcher.UIThread.Post(() => Show(target, duration ?? TimeSpan.FromMilliseconds(280)));
    }

    private void Show(PxRect target, TimeSpan duration)
    {
        _timer?.Stop();

        _window ??= CreateWindow();

        _window.Position = new PixelPoint(target.X, target.Y);
        _window.Width = target.Width;
        _window.Height = target.Height;

        if (!_window.IsVisible) _window.Show();
        _window.Opacity = 1;

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            if (_window is null) return;

            _window.Opacity = 0;

            // Hide once faded rather than leaving an invisible window sitting on
            // top of the desktop indefinitely.
            var hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            hide.Tick += (_, _) =>
            {
                hide.Stop();
                if (_window is { IsVisible: true }) _window.Hide();
            };

            hide.Start();
        };

        _timer.Start();
    }

    private static Window CreateWindow() => new()
    {
        // Click-through and never activated: the overlay must not steal focus
        // from the window that was just moved, which would defeat the point.
        // Avalonia 12 renamed SystemDecorations to WindowDecorations.
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
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x8B, 0xF5)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0x4C, 0x8B, 0xF5)),
        },

        Transitions =
        [
            new Avalonia.Animation.DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(220),
            },
        ],
    };

    public void Dispose()
    {
        _timer?.Stop();
        _window?.Close();
        _window = null;
    }
}
