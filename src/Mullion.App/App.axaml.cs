using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Mullion.App.Services;
using Mullion.App.ViewModels;
using Mullion.App.Views;

namespace Mullion.App;

public partial class App : Application
{
    private IAppHost? _host;
    private MainWindow? _window;
    private TrayController? _tray;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _desktop = desktop;
        _host = CreateHost();

        _tray = new TrayController();
        var hasTray = _tray.TryCreate(ShowWindow, () => _host.Rescan(), TogglePause, Quit, () => _host.Paused);

        _window = new MainWindow
        {
            DataContext = new MainWindowViewModel(_host),
            Icon = LoadWindowIcon(),
        };

        // Only detach the lifetime from the window when there is genuinely
        // somewhere else to control the app from. Without a tray icon,
        // OnExplicitShutdown plus a window that hides on close leaves the app
        // running with no way to reach it - which is exactly what happened.
        //
        // OnMainWindowClose rather than OnLastWindowClose: the zone-flash
        // overlay is a real Window that stays open between flashes, so
        // "last window" would never be reached and the app still could not exit.
        desktop.MainWindow = _window;
        desktop.ShutdownMode = hasTray
            ? ShutdownMode.OnExplicitShutdown
            : ShutdownMode.OnMainWindowClose;

        // The window only hides on close while the tray can bring it back.
        _window.HideInsteadOfClosing = hasTray;

        if (!hasTray && _tray.FailureReason is not null)
        {
            // Surfaced rather than swallowed: the app is usable without a tray
            // icon, but the user needs to know why it is not there.
            (_window.DataContext as MainWindowViewModel)?.ReportTrayFailure(_tray.FailureReason);
        }

        _host.Start();

        var startHidden = desktop.Args?.Contains("--tray") == true && hasTray;

        if (ShouldRunWizard())
        {
            ShowWizard();
        }
        else if (!startHidden)
        {
            _window.Show();
        }

        desktop.Exit += (_, _) =>
        {
            _tray?.Dispose();
            (_host as IDisposable)?.Dispose();
        };

        base.OnFrameworkInitializationCompleted();
    }

    private void TogglePause()
    {
        if (_host is null) return;

        _host.Paused = !_host.Paused;
        _tray?.SetPaused(_host.Paused);
    }

    private void Quit()
    {
        _tray?.Dispose();
        _desktop?.Shutdown();
    }

    private void ShowWindow()
    {
        if (_window is null) return;

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static WindowIcon? LoadWindowIcon()
    {
        var uri = new Uri("avares://Mullion/Assets/mullion.ico");
        if (!AssetLoader.Exists(uri)) return null;

        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }

    private bool ShouldRunWizard() =>
#if PLATFORM_WINDOWS
        OperatingSystem.IsWindows() && _host is WindowsAppHost { NeedsWizard: true };
#else
        false;
#endif

    private void ShowWizard()
    {
        if (_host is not IWizardHost wizardHost) return;

        var wizard = new WizardWindow
        {
            DataContext = new WizardViewModel(wizardHost),
            Icon = LoadWindowIcon(),
        };

#if PLATFORM_WINDOWS
        if (OperatingSystem.IsWindows() && _host is WindowsAppHost windowsHost)
            WireWizardClose(windowsHost, wizard, ShowWindow);
#endif

        wizard.Show();
    }

#if PLATFORM_WINDOWS
    /// <summary>
    /// Finishing the wizard hands over to the main window rather than leaving
    /// the app with no visible surface. Extracted so the platform attribute
    /// covers the lambda body too - the analyzer does not see a call-site guard
    /// through a closure.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void WireWizardClose(WindowsAppHost host, Window wizard, Action showWindow)
    {
        host.WizardCloseRequested = () =>
        {
            wizard.Close();
            showWindow();
            host.WizardCloseRequested = null;
        };
    }
#endif

    private static IAppHost CreateHost()
    {
#if PLATFORM_WINDOWS
        // The runtime check is what satisfies the platform analyzer; the #if
        // controls whether the assembly is referenced at all.
        if (OperatingSystem.IsWindows()) return new WindowsAppHost();
#endif

        return new DesignAppHost();
    }
}
