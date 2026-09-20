using System.Runtime.Versioning;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mullion.App.Services;
using Mullion.App.ViewModels;
using Mullion.Core.Simulation;
using Mullion.Core.Updates;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Whether a release that was found actually reaches the screen.
/// <para>
/// The check itself is tested elsewhere. What is tested here is the wiring
/// between it and the window, which is the part that fails silently: a check
/// that works perfectly and a notice nobody ever sees are indistinguishable
/// from the outside.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class UpdateNoticeTests
{
    /// <param name="realUiThread">
    /// Leave false unless the test is ABOUT which thread the answer is filed on.
    /// These tests have no message loop, so the host's own marshal would queue
    /// what it posts and nothing would ever drain it.
    /// </param>
    private static WindowsAppHost Host(bool realUiThread = false)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows host is not available here.");

        var host = new WindowsAppHost(SimulatedTopologies.Find("single-32-9"), TestConfig.Path());

        if (!realUiThread) host.OnUiThread = work => work();

        host.Start();
        return host;
    }

    [Fact]
    public void NothingIsSaidWhenTheBuildIsCurrent()
    {
        // Which is nearly always. A line that is permanently on screen saying
        // there is no news is worse than no line.
        using var host = Host();
        var vm = new MainWindowViewModel(host);

        vm.HasUpdate.ShouldBeFalse();
        vm.NewerVersion.ShouldBeNull();
    }

    [Fact]
    public async Task ASimulatedRunNeverGoesLooking()
    {
        // Simulation is a preview: nothing that touches the machine or the
        // network runs, and an outbound request is exactly that. Left ungated,
        // the test suite itself would call api.github.com several times a run.
        using var host = Host();

        var verdict = await ((ISettingsHost)host)
            .CheckForUpdatesNow(TestContext.Current.CancellationToken);

        verdict.Outcome.ShouldBe(UpdateOutcome.Unknown);
        host.GetSnapshot().NewerVersion.ShouldBeNull();
    }

    [Fact]
    public async Task AFoundReleaseReachesTheWindow()
    {
        using var host = Host();

        // A feed that answers, without one existing. What is proved is the
        // wiring - check, snapshot, view model - rather than any one property.
        host.Updates = new UpdateService(_ => Task.FromResult<string?>(
            """{ "tag_name": "v99.1.0", "html_url": "https://example.invalid/99" }"""));

        var vm = new MainWindowViewModel(host);
        vm.HasUpdate.ShouldBeFalse("nothing has been found yet");

        await ((ISettingsHost)host).CheckForUpdatesNow(TestContext.Current.CancellationToken);

        host.GetSnapshot().NewerVersion.ShouldBe("99.1.0");

        vm.HasUpdate.ShouldBeTrue("the window listens for StateChanged");
        vm.NewerVersion.ShouldBe("99.1.0");
        vm.NewerVersionUrl.ShouldBe("https://example.invalid/99");
        vm.UpdateLine.ShouldBe("Mullion 99.1.0 available");
    }

    [AvaloniaFact]
    public async Task AManualCheckFilesItsAnswerOnTheUiThread()
    {
        // The crash this exists to prevent: the check awaits the network,
        // resumes on a thread-pool thread, and files the answer from there.
        // Filing it raises StateChanged, the window refreshes, and Avalonia
        // ends the process for touching a control from a thread that does not
        // own it. The daily check posted itself back and the settings button
        // did not, so the app died only for whoever went looking.
        //
        // Needs a real UI thread to be about anything, which is what separates
        // this from every other test in the file.
        using var host = Host(realUiThread: true);

        // Genuinely asynchronous, unlike the Task.FromResult feeds above: a
        // check that completes inline never leaves the UI thread, and so could
        // not fail this way however the answer was filed.
        host.Updates = new UpdateService(async _ =>
        {
            await Task.Delay(20).ConfigureAwait(false);
            return """{ "tag_name": "v99.1.0" }""";
        });

        var strayThread = false;
        host.StateChanged += () => strayThread |= !Dispatcher.UIThread.CheckAccess();

        await ((ISettingsHost)host).CheckForUpdatesNow(TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        strayThread.ShouldBeFalse("a view model was refreshed from a background thread");
        host.GetSnapshot().NewerVersion.ShouldBe("99.1.0");
    }

    [Fact]
    public async Task AReleaseOlderThanThisBuildSaysNothing()
    {
        using var host = Host();

        host.Updates = new UpdateService(_ => Task.FromResult<string?>(
            """{ "tag_name": "v0.0.1" }"""));

        await ((ISettingsHost)host).CheckForUpdatesNow(TestContext.Current.CancellationToken);

        host.GetSnapshot().NewerVersion.ShouldBeNull();
    }

    [Fact]
    public void TheSettingIsOnByDefaultAndSurvivesBeingTurnedOff()
    {
        using var host = Host();
        var settings = (ISettingsHost)host;

        settings.GetSettings().CheckForUpdates.ShouldBeTrue();

        settings.SetCheckForUpdates(false);
        settings.GetSettings().CheckForUpdates.ShouldBeFalse();

        settings.SetCheckForUpdates(true);
        settings.GetSettings().CheckForUpdates.ShouldBeTrue();
    }

    [Fact]
    public async Task TurningTheCheckOffClearsAnythingItHadFound()
    {
        // Otherwise the notice stays on screen after being told to stop looking,
        // which is the opposite of what the switch says it does.
        using var host = Host();
        var settings = (ISettingsHost)host;

        host.Updates = new UpdateService(_ => Task.FromResult<string?>(
            """{ "tag_name": "v99.1.0" }"""));

        await settings.CheckForUpdatesNow(TestContext.Current.CancellationToken);
        host.GetSnapshot().NewerVersion.ShouldBe("99.1.0");

        settings.SetCheckForUpdates(false);

        host.GetSnapshot().NewerVersion.ShouldBeNull();
    }

    [Fact]
    public void TheWindowNamesTheVersionItFound()
    {
        var vm = new MainWindowViewModel(new DesignAppHost())
        {
            NewerVersion = "1.4.0",
        };

        vm.HasUpdate.ShouldBeTrue();
        vm.UpdateLine.ShouldBe("Mullion 1.4.0 available");
    }
}

/// <summary>
/// A decision worth pinning rather than leaving to be rediscovered.
/// </summary>
public class FourPartVersionTests
{
    [Fact]
    public void AFourPartTagIsRefusedRatherThanComparedWrongly()
    {
        // Real projects tag this way - PowerToys publishes "v0.101.2362.0" - so
        // this is not hypothetical. Accepting it would mean 1.0.0.1 and 1.0.0.2
        // compare EQUAL, and an update between them would be silently missed.
        // Refusing produces "could not check", which is visible and true.
        //
        // Safe for Mullion because its own tags come from VersionPrefix, which
        // is three numbers. If that ever changes, this test is the reason the
        // update check would stop working, and it says so here.
        ReleaseVersion.TryParse("v0.101.2362.0", out _).ShouldBeFalse();

        UpdateDecision.For("1.0.0", new ReleaseInfo("v0.101.2362.0", "https://example.invalid"))
            .Outcome.ShouldBe(UpdateOutcome.Unknown);
    }
}
