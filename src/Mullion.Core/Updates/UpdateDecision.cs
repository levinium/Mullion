namespace Mullion.Core.Updates;

/// <summary>What a check concluded.</summary>
public enum UpdateOutcome
{
    /// <summary>Nothing newer is published.</summary>
    UpToDate,

    /// <summary>There is a newer release, and it is worth saying so.</summary>
    Available,

    /// <summary>The question could not be answered - offline, refused, unreadable.</summary>
    Unknown,
}

/// <summary>A file published alongside a release.</summary>
/// <param name="Name">File name as published, e.g. "Mullion.exe".</param>
/// <param name="Url">Direct download link.</param>
/// <param name="Size">Length in bytes, as the feed reports it.</param>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>
/// A published release, reduced to what the decision and the installer need.
/// </summary>
/// <param name="Tag">The tag or release name, e.g. "v1.1.0".</param>
/// <param name="Url">Where a person goes to get it.</param>
/// <param name="IsDraft">Unpublished, and visible only to the people who wrote it.</param>
/// <param name="IsPreRelease">Marked as not ready for general use.</param>
/// <param name="Assets">The files attached to it, if any were read.</param>
public sealed record ReleaseInfo(
    string? Tag,
    string? Url,
    bool IsDraft = false,
    bool IsPreRelease = false,
    IReadOnlyList<ReleaseAsset>? Assets = null);

/// <summary>The conclusion, and what to open or install if someone acts on it.</summary>
/// <param name="Release">
/// The release this verdict is about, carried so an install can reach its
/// assets without asking again. Null unless one was found and read.
/// </param>
public readonly record struct UpdateVerdict(
    UpdateOutcome Outcome,
    ReleaseVersion Version,
    string? Url,
    ReleaseInfo? Release = null)
{
    public bool IsAvailable => Outcome == UpdateOutcome.Available;
}

/// <summary>
/// Whether a published release is one to tell the user about.
/// <para>
/// Separated from the fetching so it can be tested against every awkward answer
/// a release feed can give - a draft, a release candidate, a tag nobody can
/// parse, a version older than the one already running - none of which need a
/// network to reproduce.
/// </para>
/// </summary>
public static class UpdateDecision
{
    /// <summary>
    /// Compares what is running against what is published.
    /// </summary>
    /// <param name="currentVersion">This build's version, as <c>BuildInfo.Version</c> reports it.</param>
    /// <param name="latest">The newest published release, or null if none could be read.</param>
    /// <remarks>
    /// <para>
    /// Every uncertain case resolves to <see cref="UpdateOutcome.Unknown"/>
    /// rather than to either confident answer. An update prompt that fires on a
    /// version string it could not read sends people to download something they
    /// may already have; a false "up to date" is the quieter failure but it is
    /// still a lie. Saying so plainly costs nothing and is the only honest
    /// answer when the input did not parse.
    /// </para>
    /// <para>
    /// Drafts and pre-releases are never offered. A draft is not published at
    /// all, and someone running a finished release has said, by doing so, that
    /// they are not looking for a release candidate.
    /// </para>
    /// </remarks>
    public static UpdateVerdict For(string? currentVersion, ReleaseInfo? latest)
    {
        if (latest is null) return new UpdateVerdict(UpdateOutcome.Unknown, default, null);
        if (latest.IsDraft || latest.IsPreRelease) return new UpdateVerdict(UpdateOutcome.UpToDate, default, null);

        if (!ReleaseVersion.TryParse(latest.Tag, out var published))
            return new UpdateVerdict(UpdateOutcome.Unknown, default, null);

        // A tag that parses but is itself a pre-release is refused too, even
        // when the feed did not label it - "v1.1.0-rc1" means the same thing
        // whether or not anyone ticked the box.
        if (published.IsPreRelease) return new UpdateVerdict(UpdateOutcome.UpToDate, default, null);

        if (!ReleaseVersion.TryParse(currentVersion, out var running))
            return new UpdateVerdict(UpdateOutcome.Unknown, published, latest.Url);

        // Carried on both answers, not just the interesting one. The field says
        // what release the verdict is ABOUT, and that is just as true of "you
        // already have it" - a caller asking what the latest release contains
        // should not have to fetch the feed again to find out.
        return published.IsNewerThan(running)
            ? new UpdateVerdict(UpdateOutcome.Available, published, latest.Url, latest)
            : new UpdateVerdict(UpdateOutcome.UpToDate, published, latest.Url, latest);
    }
}
