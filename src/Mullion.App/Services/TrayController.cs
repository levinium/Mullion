using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Mullion.App.Services;

/// <summary>
/// Owns the tray icon.
/// <para>
/// Built in code rather than XAML so failures are observable. A tray icon
/// declared in App.axaml that fails to construct - a missing asset, an icon
/// format the decoder rejects - simply does not appear, with no error anywhere,
/// and the app is then left with no way to quit.
/// </para>
/// </summary>
public sealed class TrayController : IDisposable
{
    private TrayIcon? _icon;

    /// <summary>Null when the tray is present; a reason when it could not be created.</summary>
    public string? FailureReason { get; private set; }

    public bool IsPresent => _icon is not null;

    public bool TryCreate(
        Action onOpen,
        Action onRescan,
        Action onTogglePause,
        Action onQuit,
        Func<bool> isPaused)
    {
        try
        {
            var icon = LoadIcon();
            if (icon is null)
            {
                FailureReason = "The tray icon asset could not be loaded.";
                return false;
            }

            var pauseItem = new NativeMenuItem("Pause hotkeys")
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = isPaused(),
            };

            pauseItem.Click += (_, _) =>
            {
                onTogglePause();
                pauseItem.IsChecked = isPaused();
            };

            var open = new NativeMenuItem("Open Mullion");
            open.Click += (_, _) => onOpen();

            var rescan = new NativeMenuItem("Rescan displays");
            rescan.Click += (_, _) => onRescan();

            var quit = new NativeMenuItem("Quit Mullion");
            quit.Click += (_, _) => onQuit();

            _icon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "Mullion",
                IsVisible = true,
                Menu =
                [
                    open,
                    new NativeMenuItemSeparator(),
                    pauseItem,
                    rescan,
                    new NativeMenuItemSeparator(),
                    quit,
                ],
            };

            // Left-click opens the window, matching what people expect of a
            // tray utility.
            _icon.Clicked += (_, _) => onOpen();

            TrayIcon.SetIcons(Application.Current!, [_icon]);

            return true;
        }
        catch (Exception e)
        {
            FailureReason = e.Message;
            _icon = null;
            return false;
        }
    }

    public void SetPaused(bool paused)
    {
        if (_icon is null) return;
        _icon.ToolTipText = paused ? "Mullion — hotkeys paused" : "Mullion";
    }

    private static WindowIcon? LoadIcon()
    {
        var uri = new Uri("avares://Mullion/Assets/mullion.ico");
        if (!AssetLoader.Exists(uri)) return null;

        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }

    public void Dispose()
    {
        if (_icon is null) return;

        // Without this the icon can linger in the notification area as a ghost
        // until the user hovers over it.
        _icon.IsVisible = false;
        _icon.Dispose();
        _icon = null;
    }
}
