using System.Reflection;

namespace Mullion.App.Services;

/// <summary>
/// What build this is. Read from the assembly rather than hardcoded, so it
/// cannot drift from what was actually compiled.
/// </summary>
public static class BuildInfo
{
    /// <summary>Semantic version, e.g. "1.0.0".</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Short commit the build came from, or null outside a repository.</summary>
    public static string? Commit { get; } = ReadCommit();

    /// <summary>
    /// Version with the commit appended when known. This is what goes in logs
    /// and the About box: "1.0.0" alone is ambiguous across every commit that
    /// ships under it, which is exactly when a bug report needs to be precise.
    /// </summary>
    public static string Full => Commit is null ? Version : $"{Version} ({Commit})";

    private static string ReadVersion()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
            return typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        // The SDK appends "+<SourceRevisionId>"; the version is the part before.
        var plus = informational.IndexOf('+');
        return plus > 0 ? informational[..plus] : informational;
    }

    private static string? ReadCommit()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational)) return null;

        var plus = informational.IndexOf('+');
        if (plus < 0 || plus == informational.Length - 1) return null;

        var commit = informational[(plus + 1)..];
        return string.IsNullOrWhiteSpace(commit) ? null : commit;
    }
}
