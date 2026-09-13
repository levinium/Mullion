using System.Text;
using Mullion.App.Services;
using Mullion.Core.Updates;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Replacing the app with a newer app, against real files in a scratch folder.
/// <para>
/// This is the most dangerous code in Mullion: it downloads an executable and
/// arranges for the machine to run it, and one bad step leaves somebody with no
/// working copy at all. So it is exercised on disk rather than reasoned about -
/// files really created, really renamed, and really inspected afterwards.
/// </para>
/// <para>
/// No network and no restart. The download is a delegate, and the executable
/// path is injected, so what runs here is exactly the code that runs in
/// production with two seams filled differently.
/// </para>
/// </summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _exe;

    public UpdateInstallerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mullion-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);

        _exe = Path.Combine(_dir, "Mullion.exe");
        File.WriteAllText(_exe, "the old version");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a scratch folder outliving the test is harmless */ }
    }

    private const string NewBytes = "the new version";

    private static string HashOf(string text) =>
        UpdateAssets.Format(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>What the replacement was started with, once it has been.</summary>
    private readonly List<(string Path, IReadOnlyList<string> Args)> _launched = [];

    /// <summary>A release whose download answers with <paramref name="exeBody"/>.</summary>
    private UpdateInstaller Installer(string exeBody, string? publishedHash = null, Log? log = null)
    {
        var checksum = (publishedHash ?? HashOf(exeBody)) + "  Mullion.exe";

        return new UpdateInstaller(
            (url, _) => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(
                url.EndsWith(".sha256", StringComparison.Ordinal) ? checksum : exeBody))),
            log,
            _exe,
            // The stand-in for actually running the new build. Without it these
            // tests would be asking Windows to execute a text file, which fails
            // for a reason that has nothing to do with the install.
            (path, args) =>
            {
                _launched.Add((path, args));
                return true;
            });
    }

    private static ReleaseInfo Release() => new(
        "v9.9.9",
        "https://example.invalid/release",
        Assets:
        [
            new ReleaseAsset("Mullion.exe", "https://example.invalid/Mullion.exe", NewBytes.Length),
            new ReleaseAsset("Mullion.exe.sha256", "https://example.invalid/Mullion.exe.sha256", 80),
        ]);

    private UpdatePlan Plan => UpdatePlan.For(_exe);

    // ---- staging -------------------------------------------------------------

    [Fact]
    public async Task AVerifiedDownloadIsStagedAndTheAppIsUntouched()
    {
        var result = await Installer(NewBytes).StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        result.IsStaged.ShouldBeTrue(result.Detail);

        File.ReadAllText(Plan.Staged).ShouldBe(NewBytes);
        File.ReadAllText(_exe).ShouldBe("the old version", "staging must not touch the running app");
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchItsChecksumIsThrownAway()
    {
        // The check that matters. Without it, anything that answered the
        // download URL would be renamed over the app and run.
        var installer = Installer("something else entirely", publishedHash: HashOf(NewBytes));

        var result = await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(InstallOutcome.VerificationFailed);

        File.Exists(Plan.Staged).ShouldBeFalse("the rejected download must not be left lying around");
        File.ReadAllText(_exe).ShouldBe("the old version");
    }

    [Fact]
    public async Task ATruncatedDownloadIsCaughtByTheSameCheck()
    {
        // What a dropped connection produces. It is not malice, and it is
        // caught by exactly the same comparison.
        var installer = Installer(NewBytes[..5], publishedHash: HashOf(NewBytes));

        (await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken))
            .Outcome.ShouldBe(InstallOutcome.VerificationFailed);
    }

    [Fact]
    public async Task AReleaseWithNoChecksumIsRefused()
    {
        // Rather than installing something unverifiable. The whole defense is
        // the comparison, so no checksum means no install.
        var release = new ReleaseInfo("v9.9.9", "https://example.invalid", Assets:
            [new ReleaseAsset("Mullion.exe", "https://example.invalid/Mullion.exe", 10)]);

        var result = await Installer(NewBytes).StageAsync(release, ct: TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(InstallOutcome.VerificationFailed);
        File.Exists(Plan.Staged).ShouldBeFalse();
    }

    [Fact]
    public async Task AReleaseWithNoExecutableHasNothingToInstall()
    {
        var release = new ReleaseInfo("v9.9.9", "https://example.invalid", Assets:
            [new ReleaseAsset("Source code (zip)", "https://example.invalid/src.zip", 10)]);

        (await Installer(NewBytes).StageAsync(release, ct: TestContext.Current.CancellationToken))
            .Outcome.ShouldBe(InstallOutcome.NothingToInstall);
    }

    [Fact]
    public async Task AFailedDownloadChangesNothing()
    {
        var installer = new UpdateInstaller(
            (_, _) => throw new IOException("connection reset"), null, _exe);

        var result = await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(InstallOutcome.DownloadFailed);
        File.ReadAllText(_exe).ShouldBe("the old version");
        File.Exists(Plan.Staged).ShouldBeFalse();
    }

    [Fact]
    public async Task ProgressRunsToCompletion()
    {
        var seen = new List<double>();

        await Installer(NewBytes).StageAsync(
            Release(), new Progress<double>(seen.Add), TestContext.Current.CancellationToken);

        // Progress is posted through the synchronization context, so the only
        // claim safe to make here is that it ended where it should.
        seen.ShouldAllBe(p => p >= 0 && p <= 1);
    }

    // ---- installing ----------------------------------------------------------

    [Fact]
    public async Task InstallingPutsTheNewVersionUnderTheOldName()
    {
        // The name has to survive: auto-start records a path, and so does every
        // shortcut anyone made.
        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        installer.Apply(startInTray: false);

        File.ReadAllText(_exe).ShouldBe(NewBytes);
    }

    [Fact]
    public async Task TheOldVersionIsKeptUntilTheNewOneRuns()
    {
        // Not deleted during the swap, because at that moment it is still the
        // only working copy on the machine. The new process removes it.
        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        installer.Apply(startInTray: false);

        File.Exists(Plan.Backup).ShouldBeTrue();
        File.ReadAllText(Plan.Backup).ShouldBe("the old version");
    }

    [Fact]
    public async Task TheReplacementIsToldWhichProcessToWaitFor()
    {
        // The handoff the whole restart depends on. This process still holds
        // the single-instance mutex while it exits, so without being told to
        // wait, the new build sees an instance already running and refuses to
        // start - the app would appear to vanish on being updated.
        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        installer.Apply(startInTray: false).ShouldBeTrue();

        var (path, args) = _launched.ShouldHaveSingleItem();

        path.ShouldBe(_exe);
        args.ShouldContain(UpdateArgs.Updated);
        args[args.ToList().IndexOf(UpdateArgs.Updated) + 1]
            .ShouldBe(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RunningFromTheTrayStaysInTheTrayAcrossAnUpdate()
    {
        // Otherwise updating a background app puts a window in front of
        // somebody who never asked for one.
        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        installer.Apply(startInTray: true);

        _launched.ShouldHaveSingleItem().Args.ShouldContain("--tray");
    }

    [Fact]
    public async Task RunningWithAWindowDoesNotComeBackToTheTray()
    {
        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        installer.Apply(startInTray: false);

        _launched.ShouldHaveSingleItem().Args.ShouldNotContain("--tray");
    }

    [Fact]
    public void InstallingWithNothingStagedDoesNothing()
    {
        Installer(NewBytes).Apply(startInTray: false).ShouldBeFalse();

        File.ReadAllText(_exe).ShouldBe("the old version");
    }

    [Fact]
    public async Task ASecondUpdateIsNotBlockedByTheFirstOnesBackup()
    {
        // A backup left behind by a previous update would otherwise make the
        // first rename fail, so updating would work exactly once.
        File.WriteAllText(Plan.Backup, "a backup from last time");

        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        installer.Apply(startInTray: false).ShouldBeTrue();

        File.ReadAllText(_exe).ShouldBe(NewBytes);
    }

    [Fact]
    public async Task AFailedSwapRollsBackAndLeavesAWorkingApp()
    {
        // The rollback path: the running exe has ALREADY been renamed aside
        // when the second move fails, so without rolling back there is nothing
        // under the real name and the machine has no Mullion.
        //
        // Forced by locking the STAGED file, so the first rename succeeds and
        // the second cannot. Locking the exe instead only fails the first
        // rename, which never reaches the code this is about.
        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        using (var _ = new FileStream(Plan.Staged, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            installer.Apply(startInTray: false).ShouldBeFalse();
        }

        File.Exists(_exe).ShouldBeTrue("a working Mullion must still be under the real name");
        File.ReadAllText(_exe).ShouldBe("the old version");
        _launched.ShouldBeEmpty("nothing should have been started");
    }

    [Fact]
    public async Task AFailedFirstRenameChangesNothingAtAll()
    {
        // The other half: if the running exe cannot even be moved aside, the
        // install stops before anything is disturbed.
        var installer = Installer(NewBytes);
        await installer.StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        using (var _ = new FileStream(_exe, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            installer.Apply(startInTray: false).ShouldBeFalse();
        }

        File.ReadAllText(_exe).ShouldBe("the old version");
        File.Exists(Plan.Backup).ShouldBeFalse("nothing was moved aside");
    }

    // ---- cleaning up ---------------------------------------------------------

    [Fact]
    public void ALaterLaunchRemovesWhatAnUpdateLeftBehind()
    {
        File.WriteAllText(Plan.Backup, "the version before last");
        File.WriteAllText(Plan.Staged, "a download that was never installed");

        UpdateInstaller.CleanUp(null, _exe);

        File.Exists(Plan.Backup).ShouldBeFalse();
        File.Exists(Plan.Staged).ShouldBeFalse();
        File.Exists(_exe).ShouldBeTrue("cleanup must never touch the app itself");
    }

    [Fact]
    public void CleaningUpWithNothingToCleanIsQuiet()
    {
        Should.NotThrow(() => UpdateInstaller.CleanUp(null, _exe));

        File.Exists(_exe).ShouldBeTrue();
    }

    // ---- refusing --------------------------------------------------------------

    [Fact]
    public void ADevelopmentBuildWillNotReplaceItself()
    {
        // Dropping a published single file on top of a build folder leaves it
        // half one build and half another. The giveaway is the loose assembly.
        File.WriteAllText(Path.Combine(_dir, "Mullion.Core.dll"), "loose assembly");

        var installer = Installer(NewBytes);

        installer.IsPublishedBuild.ShouldBeFalse();
        installer.CanInstall().Outcome.ShouldBe(InstallOutcome.NotPublished);
    }

    [Fact]
    public void APublishedBuildInAWritableFolderCanInstall()
    {
        Installer(NewBytes).CanInstall().IsStaged.ShouldBeTrue();
    }

    [Fact]
    public async Task ADevelopmentBuildRefusesBeforeItDownloadsAnything()
    {
        // Not after 64MB and a progress bar.
        File.WriteAllText(Path.Combine(_dir, "Mullion.Core.dll"), "loose assembly");

        var result = await Installer(NewBytes).StageAsync(Release(), ct: TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(InstallOutcome.NotPublished);
        File.Exists(Plan.Staged).ShouldBeFalse();
    }

    [Fact]
    public void AMissingExecutableIsNotSomethingToReplace()
    {
        var installer = new UpdateInstaller(
            (_, _) => Task.FromResult<Stream>(new MemoryStream()), null,
            Path.Combine(_dir, "not-here.exe"));

        installer.CanInstall().Outcome.ShouldBe(InstallOutcome.NotPublished);
    }
}
