namespace Mullion.Core.Updates;

/// <summary>
/// The three paths a self-update moves between, and the order it moves them in.
/// <para>
/// Windows will not let a running executable be overwritten or deleted - the
/// loader holds the image - but it WILL let one be renamed. That single fact is
/// what makes replacing the app in place possible, and it dictates the whole
/// sequence: move the running exe aside, move the new one into the name it
/// vacated, start it, and leave. The old file is deleted by the new process,
/// which is the first moment anything is able to.
/// </para>
/// <para>
/// Staged beside the executable rather than in the temp directory, on purpose.
/// A rename is atomic within a volume and a copy between volumes is not, so
/// staging elsewhere would turn the critical step into a 64MB copy that can
/// fail halfway with the old exe already moved aside.
/// </para>
/// </summary>
/// <param name="Current">The running executable, and the name the new one takes.</param>
/// <param name="Staged">Where the download lands before it is verified.</param>
/// <param name="Backup">Where the running executable is moved aside to.</param>
public sealed record UpdatePlan(string Current, string Staged, string Backup)
{
    /// <summary>Suffix on the file left behind for the next launch to remove.</summary>
    public const string BackupSuffix = ".old";

    /// <summary>Suffix on a download that has not been verified or installed yet.</summary>
    public const string StagedSuffix = ".new";

    /// <summary>The directory everything happens in.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Current) ?? string.Empty;

    /// <summary>
    /// Builds the plan for a given executable.
    /// </summary>
    /// <remarks>
    /// Fixed names rather than a random temp name, so a crash between any two
    /// steps leaves files the NEXT launch recognizes and can clean up. A random
    /// name would leave 64MB of litter that nothing ever attributes to Mullion.
    /// </remarks>
    public static UpdatePlan For(string currentExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExePath);

        return new UpdatePlan(
            currentExePath,
            currentExePath + StagedSuffix,
            currentExePath + BackupSuffix);
    }

    /// <summary>
    /// Files a previous update may have left behind, for a fresh launch to tidy.
    /// <para>
    /// Both are safe to delete unconditionally at startup: reaching this point
    /// means an executable started under <see cref="Current"/>, so the backup
    /// has already been superseded and any staged download was never installed.
    /// </para>
    /// </summary>
    public IEnumerable<string> Leftovers()
    {
        yield return Backup;
        yield return Staged;
    }
}
