using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mullion.Core.Updates;

/// <summary>
/// A released version, and whether one is newer than another.
/// <para>
/// This exists because the obvious comparison is wrong. Ordering release names
/// as text puts "1.10.0" before "1.9.0", which would tell everyone on 1.9.0
/// that they are up to date and then never mention another release again. The
/// numbers have to be compared as numbers.
/// </para>
/// <para>
/// Strictly a subset of semantic versioning: three numbers and an optional
/// pre-release suffix. Build metadata is parsed and then discarded, because
/// semver says it takes no part in ordering - two builds of the same version
/// are the same version, whatever commit each came from.
/// </para>
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<ReleaseVersion>
{
    /// <summary>Whether this is a pre-release rather than a finished one.</summary>
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    /// <summary>
    /// Reads a version out of a release name or tag.
    /// <para>
    /// Tolerates the leading "v" that tag names conventionally carry, and any
    /// surrounding space, since these come from a field a human typed.
    /// </para>
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out ReleaseVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span[1..];

        // Build metadata never affects ordering, so it is dropped here rather
        // than carried around as a field nothing is allowed to look at.
        var plus = span.IndexOf('+');
        if (plus >= 0) span = span[..plus];

        string? pre = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            pre = span[(dash + 1)..];
            span = span[..dash];
            if (pre.Length == 0) return false;
        }

        var parts = span.Split('.');
        if (parts.Length is < 1 or > 3) return false;

        // "1" and "1.2" are accepted as 1.0.0 and 1.2.0. A release named that way
        // is unambiguous about what it means, and refusing it would silently stop
        // the update check rather than visibly fail.
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return false;

            numbers[i] = n;
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    /// <summary>Whether this version is one someone on <paramref name="other"/> should hear about.</summary>
    public bool IsNewerThan(ReleaseVersion other) => CompareTo(other) > 0;

    public int CompareTo(ReleaseVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Pre-release ordering, per semver.
    /// <para>
    /// The rule that matters: a version WITH a pre-release suffix is older than
    /// the same version without one, because 1.1.0-rc1 is what comes before
    /// 1.1.0. Getting this backwards would offer people on the finished release
    /// a downgrade to the release candidate.
    /// </para>
    /// </summary>
    private static int ComparePreRelease(string? left, string? right)
    {
        var leftIsFinal = string.IsNullOrEmpty(left);
        var rightIsFinal = string.IsNullOrEmpty(right);

        if (leftIsFinal && rightIsFinal) return 0;
        if (leftIsFinal) return 1;
        if (rightIsFinal) return -1;

        var a = left!.Split('.');
        var b = right!.Split('.');

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bn);

            // Numeric identifiers compare as numbers, and always rank below
            // alphanumeric ones - so "rc.2" beats "rc.10" is wrong and "rc.10"
            // beating "rc.2" is right.
            if (aNumeric && bNumeric)
            {
                if (an != bn) return an.CompareTo(bn);
                continue;
            }

            if (aNumeric) return -1;
            if (bNumeric) return 1;

            var text = string.CompareOrdinal(a[i], b[i]);
            if (text != 0) return text < 0 ? -1 : 1;
        }

        // Everything matched as far as the shorter one goes, so the one with
        // more identifiers is further along: "rc.1" precedes "rc.1.2".
        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() =>
        IsPreRelease
            ? $"{Major}.{Minor}.{Patch}-{PreRelease}"
            : $"{Major}.{Minor}.{Patch}";
}
