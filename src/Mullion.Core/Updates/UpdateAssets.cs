using System.Globalization;

namespace Mullion.Core.Updates;

/// <summary>
/// Which of a release's files to fetch, and whether what arrived is what was
/// published.
/// <para>
/// Separated from the downloading because this is where a self-update goes
/// wrong in ways a network never reproduces: a release with the exe missing, a
/// second asset whose name merely contains "Mullion.exe", a checksum file in an
/// unexpected shape. All of it is string handling, and all of it is tested.
/// </para>
/// </summary>
public static class UpdateAssets
{
    /// <summary>What the published executable is called.</summary>
    public const string ExeName = "Mullion.exe";

    /// <summary>The checksum published beside it.</summary>
    public const string ChecksumName = "Mullion.exe.sha256";

    /// <summary>
    /// A ceiling on what will be downloaded.
    /// <para>
    /// The exe is about 64MB and grows slowly. This is not a tight bound, it is
    /// a guard: something answering the download URL with an endless stream
    /// should fill a disk no further than this before being abandoned.
    /// </para>
    /// </summary>
    public const long MostBytes = 512L * 1024 * 1024;

    /// <summary>The executable to install, or null if the release has none.</summary>
    /// <remarks>
    /// Matched on the whole name, not a substring. "Mullion.exe.sha256" contains
    /// "Mullion.exe", and picking that would download 64 bytes of text, rename
    /// it over the app and leave the machine with no working Mullion at all.
    /// </remarks>
    public static ReleaseAsset? Executable(IReadOnlyList<ReleaseAsset>? assets) =>
        assets?.FirstOrDefault(a =>
            string.Equals(a.Name, ExeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The checksum file, or null if the release has none.</summary>
    public static ReleaseAsset? Checksum(IReadOnlyList<ReleaseAsset>? assets) =>
        assets?.FirstOrDefault(a =>
            string.Equals(a.Name, ChecksumName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the hash out of a checksum file.
    /// <para>
    /// The format written by the release workflow is the one <c>sha256sum</c>
    /// uses: the hash, whitespace, then the file it describes. Only the hash is
    /// taken, so a file with CRLF endings, a leading BOM or a "*" binary marker
    /// still reads.
    /// </para>
    /// </summary>
    public static bool TryReadChecksum(string? text, out string hash)
    {
        hash = string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return false;

        var first = text
            .TrimStart('﻿')
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(first)) return false;

        var token = first.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (token is null || token.Length != 64) return false;

        foreach (var c in token)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }

        hash = token.ToLowerInvariant();
        return true;
    }

    /// <summary>Renders a computed hash the way the checksum file writes it.</summary>
    public static string Format(byte[] hash) =>
        string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Whether a downloaded file is the one that was published.
    /// </summary>
    /// <remarks>
    /// Case-insensitive because the two sides are written by different tools -
    /// PowerShell's Get-FileHash returns upper case, this compares against
    /// lower - and an ordinal comparison of those would reject every genuine
    /// update while looking exactly like a tampered download.
    /// </remarks>
    public static bool Matches(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected) &&
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
}
