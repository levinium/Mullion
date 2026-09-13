using System.Diagnostics;
using System.Runtime.Versioning;
using Mullion.App.Services;
using Mullion.Core.Updates;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// The self-update, run for real: the live release feed, the real 66MB download
/// off GitHub, the published checksum, and the rename dance performed on an
/// actual Mullion.exe.
/// <para>
/// Everything else about updating is tested with injected streams and stand-in
/// files, which is right for a suite that has to pass offline in CI. But an
/// updater that has only ever been exercised against fakes is an updater nobody
/// has seen work, and this one replaces the program on a user's disk. So it gets
/// one test that uses nothing fake at all.
/// </para>
/// <para>
/// Skipped unless <c>MULLION_LIVE_UPDATE</c> names a published Mullion.exe to
/// update FROM - deliberately opt-in, because it needs a network, moves tens of
/// megabytes, and depends on a release existing. CI never runs it.
/// </para>
/// <para>
/// Nothing about the release is faked. The trick that makes it work is that the
/// version being compared is passed in: told it is 0.9.0, the real decision
/// logic finds the real 1.0.0 newer and the real installer fetches the real
/// asset. The only fiction is one string.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LiveUpdateTests : IDisposable
{
    private readonly string _dir;

    public LiveUpdateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mullion-live-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a scratch folder outliving the test is harmless */ }
    }

    private static string? OldBuild => Environment.GetEnvironmentVariable("MULLION_LIVE_UPDATE");

    [Fact]
    public async Task AnOlderMullionUpdatesItselfToTheLatestRelease()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(OldBuild),
            "Set MULLION_LIVE_UPDATE to a published Mullion.exe to run the live update test.");

        Assert.SkipUnless(File.Exists(OldBuild), $"No such file: {OldBuild}");

        var ct = TestContext.Current.CancellationToken;

        // A copy, so the build being updated from survives to be used again.
        var exe = Path.Combine(_dir, "Mullion.exe");
        File.Copy(OldBuild!, exe);

        var before = FileVersionInfo.GetVersionInfo(exe).FileVersion;
        (before ?? "").StartsWith("0.9", StringComparison.Ordinal)
            .ShouldBeTrue($"this test updates FROM a 0.9.x build, but found {before}");

        // ---- the real feed --------------------------------------------------

        var verdict = await new UpdateService().CheckAsync(ct);

        verdict.Outcome.ShouldNotBe(UpdateOutcome.Unknown,
            "the live release feed could not be read - offline, or rate limited");

        // The running assembly is whatever the test host is, so the comparison
        // is redone against the version being pretended. Same decision code.
        var asOldBuild = UpdateDecision.For("0.9.0", verdict.Release);

        asOldBuild.IsAvailable.ShouldBeTrue("the published release should be newer than 0.9.0");
        asOldBuild.Release.ShouldNotBeNull();

        UpdateAssets.Executable(asOldBuild.Release.Assets)
            .ShouldNotBeNull("the release must have a Mullion.exe attached to install");

        UpdateAssets.Checksum(asOldBuild.Release.Assets)
            .ShouldNotBeNull("the release must publish a checksum, or the download cannot be verified");

        // ---- the real download ----------------------------------------------

        var launched = new List<(string Path, IReadOnlyList<string> Args)>();

        // The real fetch - only the launch is stood in for, so a second Mullion
        // is not started at the test runner mid-suite.
        var installer = new UpdateInstaller(
            log: null,
            exePath: exe,
            launch: (path, args) => { launched.Add((path, args)); return true; });

        var seen = new List<double>();
        var staged = await installer.StageAsync(asOldBuild.Release, new Progress<double>(seen.Add), ct);

        staged.IsStaged.ShouldBeTrue(
            $"the live download did not verify: {staged.Outcome} {staged.Detail}");

        var plan = UpdatePlan.For(exe);

        File.Exists(plan.Staged).ShouldBeTrue();
        new FileInfo(plan.Staged).Length.ShouldBeGreaterThan(1_000_000,
            "a real Mullion.exe is tens of megabytes");

        new FileInfo(exe).Length.ShouldBe(new FileInfo(OldBuild!).Length,
            "staging must not have touched the installed copy");

        // ---- the real swap ---------------------------------------------------

        installer.Apply(startInTray: false).ShouldBeTrue();

        var after = FileVersionInfo.GetVersionInfo(exe).FileVersion;

        after.ShouldNotBe(before, "the executable should have been replaced");
        (after ?? "").StartsWith("1.", StringComparison.Ordinal)
            .ShouldBeTrue($"the installed version should now be the published one, but is {after}");

        File.Exists(plan.Staged).ShouldBeFalse("the staged file took the real name");
        File.Exists(plan.Backup).ShouldBeTrue("the old build is kept until the new one runs");

        // The handoff the restart depends on.
        var (path, args) = launched.ShouldHaveSingleItem();
        path.ShouldBe(exe);
        args.ShouldContain(UpdateArgs.Updated);

        // ---- and it actually runs --------------------------------------------

        var started = Process.Start(new ProcessStartInfo(exe, "--tray") { UseShellExecute = false });
        started.ShouldNotBeNull();

        try
        {
            // Long enough to get past construction and installing the hook. A
            // build that cannot start exits well inside this.
            await Task.Delay(TimeSpan.FromSeconds(10), ct);

            started!.HasExited.ShouldBeFalse(
                "the updated Mullion exited immediately instead of running");
        }
        finally
        {
            try { if (!started!.HasExited) started.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            started!.Dispose();
        }

        // Cleanup only works once the process using it has gone, which is the
        // whole reason it is deferred to the next launch.
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        UpdateInstaller.CleanUp(null, exe);

        File.Exists(plan.Backup).ShouldBeFalse("the next launch removes the old build");
    }
}
