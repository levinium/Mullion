using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Mullion.Core.Updates;

namespace Mullion.App.Services;

/// <summary>
/// Finds out whether a newer release has been published.
/// <para>
/// The whole of the network side is here, and it is deliberately small: one GET
/// with no body, no query string, no identifier, and no record kept of who
/// asked. What goes out is an HTTP request for a public file. Nothing about the
/// machine, the layout, the displays or the keys ever leaves.
/// </para>
/// <para>
/// That restraint is not decoration. This app installs a low-level keyboard
/// hook, which is the same mechanism a keylogger uses, and the only thing
/// separating the two from outside is what the process sends and what it keeps.
/// A check that quietly carried a machine id would be indistinguishable from
/// the thing people are right to be afraid of.
/// </para>
/// </summary>
public sealed class UpdateService
{
    /// <summary>
    /// Long enough for a slow connection, short enough that nothing waits on it.
    /// Nothing blocks on this check, so the only cost of the timeout expiring is
    /// that the question goes unanswered until next time.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = CreateClient();

    private readonly Func<CancellationToken, Task<string?>> _fetch;
    private readonly Log? _log;

    /// <summary>The release feed this build reads, or null if it was built without one.</summary>
    public static string? FeedUrl { get; } = ReadMetadata("UpdateFeedUrl");

    /// <summary>Where a person is sent to actually get the new version.</summary>
    public static string? PageUrl { get; } = ReadMetadata("UpdatePageUrl");

    /// <summary>Whether this build can check at all.</summary>
    public static bool IsAvailable => !string.IsNullOrWhiteSpace(FeedUrl);

    public UpdateService(Log? log = null)
    {
        _log = log;
        _fetch = FetchAsync;
    }

    /// <summary>Takes the fetch as a delegate so the decision can be tested without a network.</summary>
    public UpdateService(Func<CancellationToken, Task<string?>> fetch, Log? log = null)
    {
        _fetch = fetch;
        _log = log;
    }

    /// <summary>
    /// Asks once, and answers with what it found.
    /// </summary>
    /// <remarks>
    /// Never throws. Every way this can fail - offline, DNS, a rate limit, a
    /// proxy returning a login page, malformed JSON - is the same answer to the
    /// user, which is that the question could not be answered right now. A
    /// background version check has no business raising an exception into
    /// anything, least of all an app whose real job is servicing a keyboard hook.
    /// </remarks>
    public async Task<UpdateVerdict> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _fetch(ct).ConfigureAwait(false);
            return UpdateDecision.For(BuildInfo.Version, ParseRelease(json));
        }
        catch (Exception ex)
        {
            _log?.Info($"Update check did not complete: {ex.GetType().Name}");
            return UpdateDecision.For(BuildInfo.Version, null);
        }
    }

    /// <summary>
    /// Pulls the four fields that matter out of a release document.
    /// <para>
    /// Hand-read rather than deserialized into a type, because the response
    /// carries several dozen fields this app has no interest in and binding to
    /// them would make an unrelated change upstream into a parse failure here.
    /// </para>
    /// </summary>
    public static ReleaseInfo? ParseRelease(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var root = doc.RootElement;

            // The tag is the version; the name is a title someone typed and may
            // be anything at all. The tag is tried first for that reason, but a
            // release with only a name is still worth reading.
            var tag = Text(root, "tag_name") ?? Text(root, "name");
            if (tag is null) return null;

            return new ReleaseInfo(
                tag,
                Text(root, "html_url"),
                Flag(root, "draft"),
                Flag(root, "prerelease"),
                ReadAssets(root));
        }
        catch (JsonException)
        {
            // A proxy or captive portal answering with HTML is the usual cause,
            // and it means exactly what a failed request means.
            return null;
        }
    }

    /// <summary>
    /// The files attached to a release.
    /// </summary>
    /// <remarks>
    /// An asset is skipped unless it has a name, a link and a state of
    /// "uploaded". GitHub lists an asset from the moment an upload STARTS, so a
    /// release being published right now can advertise a file that is not all
    /// there yet - and downloading one of those produces a truncated executable
    /// that fails its checksum, which is the right outcome by the slowest
    /// possible route.
    /// </remarks>
    private static IReadOnlyList<ReleaseAsset> ReadAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return [];

        var found = new List<ReleaseAsset>();

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;

            var name = Text(asset, "name");
            var url = Text(asset, "browser_download_url");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;

            var state = Text(asset, "state");
            if (state is not null && !string.Equals(state, "uploaded", StringComparison.Ordinal)) continue;

            var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt64()
                : 0;

            found.Add(new ReleaseAsset(name, url, size));
        }

        return found;
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private async Task<string?> FetchAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FeedUrl)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        using var response = await Http
            .GetAsync(FeedUrl, HttpCompletionOption.ResponseContentRead, timeout.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _log?.Info($"Update check returned {(int)response.StatusCode}.");
            return null;
        }

        return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout };

        // GitHub refuses a request with no User-Agent outright, so this is
        // required rather than polite. It names the product and version and
        // nothing else - no machine, no user, no install id.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Mullion", BuildInfo.Version));

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }

    private static string? ReadMetadata(string key)
    {
        var value = typeof(UpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
