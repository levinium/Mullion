using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mullion.App.Services;
using Mullion.App.ViewModels;
using Mullion.App.Views;

namespace Mullion.App;

public partial class App : Application
{
    private IAppHost? _host;
    private MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Closing the window hides it; Mullion keeps running in the tray, so the
        // process must only exit when explicitly asked to.
        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

        _host = CreateHost();

        DataContext = new TrayViewModel(_host, ShowWindow, () => desktop.Shutdown());

        _window = new MainWindow { DataContext = new MainWindowViewModel(_host) };

        _host.Start();

        // --tray starts minimised to the tray, which is how the auto-start entry
        // launches it; a manual launch shows the window.
        var startHidden = desktop.Args?.Contains("--tray") == true;

        if (ShouldRunWizard())
        {
            ShowWizard();
        }
        else if (!startHidden)
        {
            _window.Show();
        }

        desktop.Exit += (_, _) => (_host as IDisposable)?.Dispose();

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow()
    {
        if (_window is null) return;

        _window.Show();
        _window.WindowState = Avalonia.Controls.WindowState.Normal;
        _window.Activate();
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

        var wizard = new WizardWindow { DataContext = new WizardViewModel(wizardHost) };

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
    private static void WireWizardClose(WindowsAppHost host, Avalonia.Controls.Window wizard, Action showWindow)
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
        // controls whether the assembly is referenced at all. Both are needed,
        // and that is the boundary keeping a macOS backend a drop-in job.
        if (OperatingSystem.IsWindows()) return new WindowsAppHost();
#endif

        // macOS and Linux backends are not implemented yet; the design host at
        // least lets the UI run so layout work is not Windows-gated.
        return new DesignAppHost();
    }
}
