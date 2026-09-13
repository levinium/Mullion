using Mullion.App.Services;
using Mullion.Core.Config;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// A config file that belongs to one test and to nobody else.
/// <para>
/// The app host used to construct its own store with no path, which is the real
/// one under %APPDATA%. Every test that built a host therefore loaded, and could
/// save, the configuration of whoever was running the tests - and because a test
/// host is handed a simulated desk, its fingerprint never matched the saved
/// profile, so the host generated a fresh one and wrote it over theirs. A test
/// run reset the key surface and every zone edit on the developer's own machine,
/// silently, and the only sign was the diagram looking different afterwards.
/// </para>
/// </summary>
internal static class TestConfig
{
    private static readonly string Root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "mullion-tests", Guid.NewGuid().ToString("N"));

    /// <summary>A path no other test shares, so nothing here depends on order.</summary>
    public static string Path()
    {
        System.IO.Directory.CreateDirectory(Root);
        return System.IO.Path.Combine(Root, $"{Guid.NewGuid():N}.json");
    }
}

/// <summary>
/// The guard for the above: proof that a host built on a pretend desk cannot be
/// pointed at the real configuration even by accident.
/// </summary>
public class ConfigIsolationTests
{
    [Fact]
    public void ASimulatedDeskNeverWritesTheRealConfig()
    {
        // The default is what a plain `new WindowsAppHost(topology)` gets, which
        // is what the test suite and every --simulate screenshot pass used.
        var simulated = ConfigStore.ForSimulation("single-32-9");

        simulated.ShouldNotBe(ConfigStore.DefaultPath);
        System.IO.Path.GetDirectoryName(simulated)
            .ShouldNotBe(System.IO.Path.GetDirectoryName(ConfigStore.DefaultPath));
    }

    [Fact]
    public void EachSimulatedDeskKeepsItsOwn()
    {
        // Otherwise switching simulations mid-session carries one desk's zones
        // onto another, which looks exactly like a layout bug.
        ConfigStore.ForSimulation("single-32-9")
            .ShouldNotBe(ConfigStore.ForSimulation("dual-16-9"));
    }

    [Fact]
    public void TheRealDeskStillGetsTheRealConfig()
    {
        // The isolation must not have been bought by sending the actual app
        // somewhere else - that would lose everyone's settings instead.
        new ConfigStore().Path_.ShouldBe(ConfigStore.DefaultPath);
    }
}
