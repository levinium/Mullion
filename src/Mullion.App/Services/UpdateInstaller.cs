using System.Net.Http;
using System.Security.Cryptography;
using Mullion.Core.Updates;

namespace Mullion.App.Services;

/// <summary>How far an install got, and why it stopped there.</summary>
public enum InstallOutcome
{
    /// <summary>The new executable is downloaded, verified and staged.</summary>
    Staged,

    /// <summary>The release has no executable attached to install.</summary>
    NothingToInstall,

    /// <summary>Where the app lives cannot be written to without administrator rights.</summary>
    NotWritable,

    /// <summary>This build cannot replace itself - it is not a published single file.</summary>
    NotPublished,

    /// <summary>The download did not finish.</summary>
    DownloadFailed,

    /// <summary>What arrived is not what was published.</summary>
    VerificationFailed,

    /// <summary>Called off before it finished.</summary>
    Canceled,
}

/// <param name="Outcome">How far it got.</param>
/// <param name="Detail">A sentence for the user, or null when there is nothing to add.</param>
public readonly record struct InstallResult(InstallOutcome Outcome, string? Detail = null)
{
    public bool IsStaged => Outcome == InstallOutcome.Staged;
}

/// <summary>
/// Replaces Mullion with a newer Mullion, in place.
/// <para>
/// Worth stating plainly what this does, because it is the most dangerous thing
/// in the app: it downloads an executable and arranges for the machine to run
/// it. Everything here exists to narrow that down to "the file the project
/// published, or nothing at all".
/// </para>
/// <para>
/// Three checks stand between the two. The download is HTTPS, so it comes from
/// the release host or it does not come. Its SHA256 must equal the checksum
/// published beside it. And if the RUNNING executable carries a valid signature,
/// the replacement must carry a valid signature from the same publisher - which
/// does nothing while the build is unsigned, and becomes the real defense the
/// day it is signed, without anyone having to remember to turn it on.
/// </para>
/// <para>
/// The install itself is two renames, in an order chosen so that no single
/// failure leaves the machine without a working Mullion. See <see cref="Apply"/>.
/// </para>
/// </summary>
public sealed class UpdateInstaller
{
    private readonly Log? _log;
    private readonly Func<string, CancellationToken, Task<Stream>> _open;
    private readonly string? _exe;

    public UpdateInstaller(Log? log = null, string? exePath = null)
        : this(OpenAsync, log, exePath) { }

    /// <summary>
    /// Takes the fetch and the executable's own path as arguments, so the rename
    /// dance can be run against a scratch directory full of stand-in files.
    /// <para>
    /// That seam is not a nicety. The one sequence in this class that can leave
    /// a machine with no working Mullion is the pair of renames in
    /// <see cref="Apply"/>, and against the real process path there is no way to
    /// exercise it - a test would be replacing the test runner.
    /// </para>
    /// </summary>
    public UpdateInstaller(
        Func<string, CancellationToken, Task<Stream>> open,
        Log? log = null,
        string? exePath = null,
        Func<string, IReadOnlyList<string>, bool>? launch = null)
    {
        _open = open;
        _log = log;
        _exe = exePath;
        _launch = launch ?? Launch;
    }

    private readonly Func<string, IReadOnlyList<string>, bool> _launch;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>The running executable, or null where there is no file to speak of.</summary>
    public string? ExePath => _exe ?? Environment.ProcessPath;

    /// <summary>
    /// Whether this build is one that can replace itself.
    /// <para>
    /// A published Mullion is a single file with everything inside it. A build
    /// from the IDE is an executable surrounded by the assemblies it needs, and
    /// dropping a published exe on top of it would leave a folder that is half
    /// one build and half another. The giveaway is whether Mullion.Core.dll is
    /// sitting next to the exe: in a published build it is inside it.
    /// </para>
    /// </summary>
    public bool IsPublishedBuild
    {
        get
        {
            var directory = Path.GetDirectoryName(ExePath);
            if (string.IsNullOrEmpty(directory)) return false;

            return !File.Exists(Path.Combine(directory, "Mullion.Core.dll"));
        }
    }

    /// <summary>
    /// Whether an install could be attempted at all, and what to say if not.
    /// <para>
    /// Asked before the button is offered, so somebody in Program Files is told
    /// to download it themselves rather than watching a progress bar finish and
    /// then fail on the one step that was never going to work.
    /// </para>
    /// </summary>
    public InstallResult CanInstall()
    {
        if (ExePath is null || !File.Exists(ExePath))
            return new InstallResult(InstallOutcome.NotPublished, "Mullion cannot find its own executable.");

        if (!IsPublishedBuild)
            return new InstallResult(InstallOutcome.NotPublished, "This is a development build; update it from source.");

        var plan = UpdatePlan.For(ExePath);

        // A write probe rather than reading permissions. The rules that decide
        // whether this succeeds - ACLs, group membership, UAC virtualization,
        // controlled folder access, a read-only share - do not reduce to a flag
        // anyone can read, and the only honest test is to try it.
        var probe = Path.Combine(plan.Directory, $".mullion-write-test-{Environment.ProcessId}");

        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new InstallResult(InstallOutcome.NotWritable,
                "Mullion is installed where it cannot update itself. Download the new version manually, or move Mullion somewhere writable.");
        }

        return new InstallResult(InstallOutcome.Staged);
    }

    /// <summary>
    /// Downloads the release's executable, verifies it, and leaves it staged.
    /// <para>
    /// Nothing about the installed app changes here. This step can fail, be
    /// cancelled or be abandoned halfway and the worst it leaves is a file that
    /// the next launch deletes.
    /// </para>
    /// </summary>
    public async Task<InstallResult> StageAsync(
        ReleaseInfo release,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var ready = CanInstall();
        if (!ready.IsStaged) return ready;

        var exe = UpdateAssets.Executable(release.Assets);
        if (exe is null)
            return new InstallResult(InstallOutcome.NothingToInstall, "That release has no Mullion.exe attached.");

        var plan = UpdatePlan.For(ExePath!);

        try
        {
            // The checksum first. Downloading 64MB before discovering there is
            // nothing to check it against wastes the time AND leaves the only
            // question that matters unanswered.
            var expected = await ReadChecksumAsync(release, ct).ConfigureAwait(false);

            if (expected is null)
            {
                return new InstallResult(InstallOutcome.VerificationFailed,
                    "That release publishes no checksum, so the download cannot be verified.");
            }

            var actual = await DownloadAsync(exe, plan.Staged, progress, ct).ConfigureAwait(false);

            if (!UpdateAssets.Matches(expected, actual))
            {
                Discard(plan.Staged);
                _log?.Warn("Update rejected: the download did not match the published checksum.");

                return new InstallResult(InstallOutcome.VerificationFailed,
                    "The download did not match the published checksum, so it was discarded.");
            }

            if (!SignatureAcceptable(plan.Staged, out var why))
            {
                Discard(plan.Staged);
                _log?.Warn($"Update rejected: {why}");

                return new InstallResult(InstallOutcome.VerificationFailed, why);
            }

            _log?.Info($"Update {release.Tag} staged and verified.");
            return new InstallResult(InstallOutcome.Staged);
        }
        catch (OperationCanceledException)
        {
            Discard(plan.Staged);
            return new InstallResult(InstallOutcome.Canceled);
        }
        catch (Exception e)
        {
            Discard(plan.Staged);
            _log?.Warn($"Update download failed: {e.GetType().Name}");

            return new InstallResult(InstallOutcome.DownloadFailed,
                "The download did not finish. Nothing was changed.");
        }
    }

    /// <summary>
    /// Puts the staged executable in place and starts it.
    /// </summary>
    /// <returns>
    /// True when the caller should now exit. False means nothing was changed and
    /// the running app should carry on.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The order is the whole design. A running executable cannot be deleted or
    /// overwritten, but it CAN be renamed, so:
    /// </para>
    /// <list type="number">
    /// <item>move the running exe to its backup name - it keeps running, the
    /// loader does not care what the file is called;</item>
    /// <item>move the staged exe into the name just vacated;</item>
    /// <item>start it, and leave.</item>
    /// </list>
    /// <para>
    /// If step 2 fails, step 1 is undone and the app is exactly as it was. The
    /// window where neither file holds the real name is one rename wide and
    /// nothing runs from disk during it. The backup is deleted by the NEW
    /// process, which is the first moment anything is allowed to.
    /// </para>
    /// </remarks>
    public bool Apply(bool startInTray)
    {
        if (ExePath is null) return false;

        var plan = UpdatePlan.For(ExePath);

        if (!File.Exists(plan.Staged))
        {
            _log?.Warn("Asked to install an update, but nothing is staged.");
            return false;
        }

        // A backup from an earlier update would block the first rename. It is
        // no longer referenced by anything, so it goes.
        Discard(plan.Backup);

        try
        {
            File.Move(plan.Current, plan.Backup);
        }
        catch (Exception e)
        {
            _log?.Error("Could not move the running executable aside; nothing was changed.", e);
            return false;
        }

        try
        {
            File.Move(plan.Staged, plan.Current);
        }
        catch (Exception e)
        {
            _log?.Error("Could not put the new executable in place; rolling back.", e);

            try { File.Move(plan.Backup, plan.Current); }
            catch (Exception rollback)
            {
                // Both names are now wrong, which is the one outcome worth
                // shouting about: the backup is still a working Mullion and
                // renaming it by hand is all anybody has to do.
                _log?.Error($"Rollback failed. A working Mullion is at {plan.Backup}.", rollback);
            }

            return false;
        }

        var args = new List<string>
        {
            UpdateArgs.Updated,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        // Carried across on purpose. Somebody who runs Mullion from the tray
        // should not have an update pop a window at them.
        if (startInTray) args.Add("--tray");

        if (_launch(plan.Current, args))
        {
            _log?.Info("Update installed; restarting.");
            return true;
        }

        // The new executable IS in place, so the next launch gets it either
        // way. Failing to restart is a worse experience, not a broken install -
        // and returning false keeps this process alive rather than quitting
        // into nothing.
        _log?.Warn("The update is installed but Mullion could not restart itself.");
        return false;
    }

    private bool Launch(string path, IReadOnlyList<string> args)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            };

            // Added individually so the runtime quotes each one. Joined by hand,
            // a path with a space in it arrives as two arguments.
            foreach (var arg in args) start.ArgumentList.Add(arg);

            return System.Diagnostics.Process.Start(start) is not null;
        }
        catch (Exception e)
        {
            _log?.Error("Could not start the updated Mullion.", e);
            return false;
        }
    }

    /// <summary>
    /// Removes what a previous update left behind. Called once at startup.
    /// </summary>
    /// <remarks>
    /// Both files are safe to delete here without knowing how they got there:
    /// reaching this code means an executable is running under the real name, so
    /// a backup has already been superseded and a staged download was never
    /// installed. The backup may still be locked for a moment by the process
    /// that started this one, which is why failing is not worth reporting - the
    /// next launch gets it.
    /// </remarks>
    public static void CleanUp(Log? log = null, string? exePath = null)
    {
        var exe = exePath ?? Environment.ProcessPath;
        if (exe is null) return;

        foreach (var leftover in UpdatePlan.For(exe).Leftovers())
        {
            try
            {
                if (!File.Exists(leftover)) continue;

                File.Delete(leftover);
                log?.Info($"Removed {Path.GetFileName(leftover)} from a previous update.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still held by the process that launched us. Next time.
            }
        }
    }

    // ---- the parts that touch the network and the disk ----------------------

    private async Task<string?> ReadChecksumAsync(ReleaseInfo release, CancellationToken ct)
    {
        var asset = UpdateAssets.Checksum(release.Assets);
        if (asset is null) return null;

        using var stream = await _open(asset.Url, ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        return UpdateAssets.TryReadChecksum(text, out var hash) ? hash : null;
    }

    /// <summary>
    /// Streams the asset to disk, hashing as it goes, and returns what it read.
    /// </summary>
    /// <remarks>
    /// Hashed in the same pass rather than by reading the file again: it is the
    /// bytes that were written that have to be vouched for, and a second pass
    /// would be answering a slightly different question about a file something
    /// else could have touched in between.
    /// </remarks>
    private async Task<string> DownloadAsync(
        ReleaseAsset asset, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        Discard(destination);

        using var source = await _open(asset.Url, ct).ConfigureAwait(false);
        using var sha = SHA256.Create();

        await using (var file = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            long total = 0;

            while (true)
            {
                var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) break;

                total += read;

                if (total > UpdateAssets.MostBytes)
                    throw new IOException("The download exceeded the size a Mullion release can be.");

                sha.TransformBlock(buffer, 0, read, null, 0);
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                if (asset.Size > 0) progress?.Report(Math.Min(1, (double)total / asset.Size));
            }

            sha.TransformFinalBlock([], 0, 0);
        }

        progress?.Report(1);

        return UpdateAssets.Format(sha.Hash!);
    }

    /// <summary>
    /// Whether the staged file's signature is good enough to replace this one.
    /// <para>
    /// The rule is relative, not absolute: whatever the running build is, the
    /// replacement must be at least as trustworthy. An unsigned build accepts an
    /// unsigned replacement, because refusing would mean no unsigned build could
    /// ever update itself. A SIGNED build accepts only a validly signed
    /// replacement from the same publisher - so signing the app turns this on
    /// by itself, and no unsigned file can ever take its place.
    /// </para>
    /// </summary>
    private bool SignatureAcceptable(string staged, out string why)
    {
        why = string.Empty;

#if PLATFORM_WINDOWS
        if (!OperatingSystem.IsWindows()) return true;

        var current = Platform.Windows.Windows.Authenticode.SignerOf(ExePath!);

        // Unsigned today. Nothing to compare against, and demanding a signature
        // the project does not yet produce would break its own updates.
        if (current is null) return true;

        var replacement = Platform.Windows.Windows.Authenticode.SignerOf(staged);

        if (replacement is null)
        {
            why = "The download is not signed, and this copy of Mullion is. It was discarded.";
            return false;
        }

        if (!string.Equals(current, replacement, StringComparison.Ordinal))
        {
            why = "The download is signed by someone else. It was discarded.";
            return false;
        }
#endif

        return true;
    }

    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing depends on this succeeding; startup cleanup gets it.
        }
    }

    private static async Task<Stream> OpenAsync(string url, CancellationToken ct)
    {
        var response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>The arguments a restart after an update is started with.</summary>
public static class UpdateArgs
{
    /// <summary>
    /// Followed by the process id to wait for. The replaced Mullion is still
    /// exiting when this one starts, and it holds the single-instance mutex
    /// until it does - so without this the new build would see an instance
    /// already running and quietly refuse to start.
    /// </summary>
    public const string Updated = "--updated";

    /// <summary>How long to wait for the old process before giving up on it.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Waits for the process named after <see cref="Updated"/>, if there is one.
    /// Called before anything else claims the mutex.
    /// </summary>
    public static void WaitForPredecessor(string[] args)
    {
        var at = Array.IndexOf(args, Updated);
        if (at < 0 || at + 1 >= args.Length) return;

        if (!int.TryParse(args[at + 1], out var pid)) return;

        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(pid);
            previous.WaitForExit(Patience);
        }
        catch (ArgumentException)
        {
            // Already gone, which is the common case and exactly what we wanted.
        }
        catch (InvalidOperationException)
        {
            // Likewise.
        }
    }
}
