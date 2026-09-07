using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Mullion.App.Tests.TestApp))]

namespace Mullion.App.Tests;

/// <summary>
/// The real application, headless.
/// <para>
/// Reused rather than reimplemented so the tests see exactly the styles that
/// ship - a test app that rebuilt them could pass while the real diagram was
/// broken, which is the failure it exists to catch. Safe because
/// OnFrameworkInitializationCompleted does nothing without a desktop lifetime:
/// no host, no keyboard hook, no config, no tray.
/// </para>
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::Mullion.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
