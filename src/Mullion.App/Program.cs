using Avalonia;
using Avalonia.Fonts.Inter;

namespace Mullion.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Before the mutex, and that order is the whole point. After an update
        // this process is started by the one it replaces, which is still exiting
        // and still holds the single-instance mutex. Claiming it first would
        // find an instance already running and quietly refuse to start - the
        // app would appear to vanish on being updated.
        Services.UpdateArgs.WaitForPredecessor(args);

        // Two instances would each install a keyboard hook and both act on the
        // same keypress, moving the window twice.
        using var instance = new Services.SingleInstance();
        if (!instance.IsPrimary) return;

        // Whatever a previous update left behind. Only safe once this process
        // holds the name, which is exactly here.
        Services.UpdateInstaller.CleanUp();

        // Before any Avalonia call: if the process is not per-monitor DPI aware,
        // every Win32 coordinate we read is virtualised and the zone maths
        // operates on lies. The manifest declares it too; this is belt and braces.
#if PLATFORM_WINDOWS
        if (OperatingSystem.IsWindows())
            Platform.Windows.Displays.WindowsDisplayProvider.EnsurePerMonitorDpiAwareness();
#endif

        // A blocking gen2 collection is the most likely cause of a hook callback
        // overrunning LowLevelHooksTimeout and being silently uninstalled.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()   // embedded: do not depend on what fonts a machine has
            .LogToTrace();
}
