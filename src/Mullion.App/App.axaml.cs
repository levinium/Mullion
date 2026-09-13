using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Mullion.App.Controls;
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
        _host = CreateHost(desktop.Args);

        _tray = new TrayController();
        var hasTray = _tray.TryCreate(ShowWindow, () => _host.Rescan(), TogglePause, Quit, () => _host.Paused);

        var mainViewModel = new MainWindowViewModel(_host);
        mainViewModel.ShowSettingsRequested = ShowSettings;
        mainViewModel.ShowSponsorRequested = ShowSponsor;

        _window = new MainWindow
        {
            DataContext = mainViewModel,
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
        //
        // MainWindow is deliberately NOT assigned here. Avalonia's desktop
        // lifetime shows whatever is in MainWindow itself, the moment this
        // method returns - so assigning it up front puts the window on screen
        // whatever we decide below, which is what made --tray do nothing on
        // every auto-start. ShowMainWindow assigns it at the point of showing
        // instead; OnMainWindowClose still works because the lifetime compares
        // by reference when a window closes, not when it starts.
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

#if PLATFORM_WINDOWS
        if (OperatingSystem.IsWindows() && _host is WindowsAppHost rerunHost)
            WireRerunWizard(rerunHost, ShowWizard);
#endif

        _host.Start();

        // --tray is what auto-start passes; the setting is for people who want a
        // hand-launch to behave the same way. Both need somewhere to go: with no
        // tray icon, hiding the window would leave the app unreachable.
        var wantsTray = desktop.Args?.Contains("--tray") == true || _host.StartInTray;

        // --show outranks both, and has to outrank the setting rather than just
        // the flag. It marks a relaunch the user asked for from a window they
        // were looking at - restarting as administrator - where disappearing
        // into the tray reads as the app having failed to come back. "Always
        // start in the tray" is about starting, and this is a continuation.
        var startHidden = wantsTray && hasTray && desktop.Args?.Contains("--show") != true;

        // --wizard opens setup on demand, for the same reason --settings exists:
        // it otherwise appears only on a machine that has never run Mullion, so
        // it is the one screen no capture could reach. Ahead of the simulation
        // check deliberately - the arrangements worth reviewing it against are
        // exactly the ones nobody here can plug in.
        if (desktop.Args?.Contains("--wizard") == true)
        {
            ShowWizard();
        }
        else if (ShouldRunWizard() && !IsSimulating())
        {
            ShowWizard();
        }
        else if (!startHidden || IsSimulating())
        {
            ShowMainWindow();
        }

        // --settings opens straight onto the settings window. It exists so the
        // capture tooling can screenshot it: the seam handles and the split
        // controls live only there, and until this flag the only picture that
        // could be taken automatically was of the main window, which has neither.
        if (desktop.Args?.Contains("--settings") == true) ShowSettings();
        // --icons opens the icon sheet. Choosing a glyph inside the app is slow
        // and partial: you see one state of one icon at a time, and the question
        // is always how it sits beside the others.
        if (desktop.Args?.Contains("--icons") == true) IconGallery.Create().Show();
        // --drag-preview holds the drag overlay open in its restoring state. It
        // is drawn only while a window is actually in the air, which is exactly
        // when nothing can be captured.
        if (desktop.Args?.Contains("--drag-preview") == true &&
            OperatingSystem.IsWindows() && _host is WindowsAppHost previewHost)
        {
            previewHost.PreviewDragOverlay();
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

        ShowMainWindow();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>
    /// Put the main window on screen, and only then hand it to the lifetime.
    /// <para>
    /// The order is the whole point. Avalonia shows whatever is assigned to
    /// MainWindow as soon as startup finishes, so a window handed over early is
    /// a window that appears - which is why a hidden start could not stay
    /// hidden. Assigning it here means the lifetime learns which window is the
    /// main one at the first moment that is true, and never before.
    /// </para>
    /// </summary>
    private void ShowMainWindow()
    {
        if (_window is null) return;

        if (_desktop is not null) _desktop.MainWindow = _window;
        _window.Show();
    }

    private static WindowIcon? LoadWindowIcon()
    {
        var uri = new Uri("avares://Mullion/Assets/mullion.ico");
        if (!AssetLoader.Exists(uri)) return null;

        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }

    private SettingsWindow? _settings;

    /// <summary>
    /// The ask, as a dialog owned by the main window rather than a window of
    /// its own in the taskbar: it is a question with two answers and no reason
    /// to outlive the moment it was asked in.
    /// </summary>
    private void ShowSponsor()
    {
        if (_window is null) return;

        var sponsor = new SponsorWindow
        {
            DataContext = new SponsorViewModel(),
            Icon = LoadWindowIcon(),
        };

        sponsor.ShowDialog(_window);
    }

    private void ShowSettings()
    {
        if (_host is not ISettingsHost settingsHost) return;

        // Reuse the window rather than stacking duplicates, and refresh it so it
        // never shows state that changed while it was closed.
        if (_settings is not null)
        {
            (_settings.DataContext as SettingsViewModel)?.Reload();
            _settings.Show();
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow
        {
            DataContext = new SettingsViewModel(settingsHost),
            Icon = LoadWindowIcon(),
        };

        _settings.Closed += (_, _) => _settings = null;
        _settings.Show(_window!);
    }

    private bool IsSimulating() =>
#if PLATFORM_WINDOWS
        OperatingSystem.IsWindows() && _host is WindowsAppHost { Simulated: not null };
#else
        false;
#endif

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
    private static void WireRerunWizard(WindowsAppHost host, Action showWizard) =>
        host.RerunWizardRequested = showWizard;

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

    private static IAppHost CreateHost(string[]? args)
    {
#if PLATFORM_WINDOWS
        // The runtime check is what satisfies the platform analyzer; the #if
        // controls whether the assembly is referenced at all.
        if (OperatingSystem.IsWindows()) return new WindowsAppHost(FindSimulation(args));
#endif

        return new DesignAppHost();
    }

    /// <summary>
    /// --simulate &lt;id&gt; previews a fake display arrangement. Most setups Mullion
    /// has to handle cannot be plugged into the machine it is developed on, so
    /// without this the multi-monitor defaults are never actually looked at.
    /// </summary>
    private static Core.Simulation.SimulatedTopology? FindSimulation(string[]? args)
    {
        if (args is null) return null;

        var index = Array.FindIndex(args, a =>
            a.Equals("--simulate", StringComparison.OrdinalIgnoreCase));

        if (index < 0 || index + 1 >= args.Length) return null;

        return Core.Simulation.SimulatedTopologies.Find(args[index + 1]);
    }
}


