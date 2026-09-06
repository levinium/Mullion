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
    private DispatcherTimer? _hold;
    private DispatcherTimer? _fade;

    /// <summary>
    /// Incremented on every flash so that callbacks belonging to a superseded
    /// one do nothing.
    /// <para>
    /// Stopping the timers is not enough on its own: a tick already queued on
    /// the dispatcher still runs after Stop(). Without this guard a stale
    /// hide-callback fires part-way through the NEXT flash and blanks it, which
    /// looks exactly like the second press having no effect.
    /// </para>
    /// </summary>
    private int _generation;

    public void Flash(PxRect target, TimeSpan? duration = null)
    {
        Dispatcher.UIThread.Post(() => Show(target, duration ?? TimeSpan.FromMilliseconds(280)));
    }

    private void Show(PxRect target, TimeSpan duration)
    {
        var generation = ++_generation;

        // Cancel both timers. A flash interrupted mid-fade must restart cleanly
        // rather than inheriting the previous one's schedule.
        _hold?.Stop();
        _fade?.Stop();

        _window ??= CreateWindow();

        _window.Position = new PixelPoint(target.X, target.Y);
        _window.Width = target.Width;
        _window.Height = target.Height;

        if (!_window.IsVisible) _window.Show();

        // Snap to full opacity rather than transitioning up: an interrupting
        // flash should appear immediately at the new zone, not fade in from
        // wherever the previous one had got to.
        SetOpacityImmediately(1);

        _hold = new DispatcherTimer { Interval = duration };
        _hold.Tick += (_, _) =>
        {
            _hold?.Stop();
            if (generation != _generation || _window is null) return;

            _window.Opacity = 0;   // transitions out

            _fade = new DispatcherTimer { Interval = FadeDuration };
            _fade.Tick += (_, _) =>
            {
                _fade?.Stop();
                if (generation != _generation) return;
                if (_window is { IsVisible: true }) _window.Hide();
            };

            _fade.Start();
        };

        _hold.Start();
    }

    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// Set opacity without the fade transition, so an interrupting flash is
    /// visible at once.
    /// </summary>
    private void SetOpacityImmediately(double value)
    {
        if (_window is null) return;

        var transitions = _window.Transitions;
        _window.Transitions = null;
        _window.Opacity = value;
        _window.Transitions = transitions;
    }

    private static Window CreateWindow() => new()
    {
        // Click-through and never activated: the overlay must not steal focus
        // from the window that was just moved, which would defeat the point.
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
                Duration = FadeDuration,
            },
        ],
    };

    public void Dispose()
    {
        _generation++;   // invalidate anything still queued
        _hold?.Stop();
        _fade?.Stop();
        _window?.Close();
        _window = null;
    }
}
