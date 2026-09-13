using Mullion.App.Services;
using Mullion.Core.Updates;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// Reading a release feed, and surviving the answers that are not one.
/// <para>
/// No network. The fetch is a delegate precisely so the interesting cases -
/// offline, a captive portal, a rate limit, JSON that is not what was expected -
/// can be reproduced exactly rather than waited for.
/// </para>
/// </summary>
public class UpdateServiceTests
{
    /// <summary>A response shaped like the one GitHub actually returns, trimmed to what is read.</summary>
    private const string RealShape = """
        {
          "url": "https://api.github.com/repos/o/r/releases/1",
          "html_url": "https://github.com/o/r/releases/tag/v1.2.0",
          "id": 1,
          "tag_name": "v1.2.0",
          "name": "Mullion 1.2.0",
          "draft": false,
          "prerelease": false,
          "created_at": "2026-09-01T00:00:00Z",
          "assets": [ { "name": "Mullion.exe", "size": 66000000 } ],
          "body": "Fixes a thing."
        }
        """;

    private static UpdateService Returning(string? body) =>
        new(_ => Task.FromResult(body));

    [Fact]
    public void TheFieldsThatMatterAreReadOutOfARealResponse()
    {
        var release = UpdateService.ParseRelease(RealShape);

        release.ShouldNotBeNull();
        release.Tag.ShouldBe("v1.2.0");
        release.Url.ShouldBe("https://github.com/o/r/releases/tag/v1.2.0");
        release.IsDraft.ShouldBeFalse();
        release.IsPreRelease.ShouldBeFalse();
    }

    [Fact]
    public void TheDraftAndPreReleaseFlagsAreRead()
    {
        var release = UpdateService.ParseRelease(
            """{ "tag_name": "v9.0.0", "draft": true, "prerelease": true }""");

        release.ShouldNotBeNull();
        release.IsDraft.ShouldBeTrue();
        release.IsPreRelease.ShouldBeTrue();
    }

    [Fact]
    public void AReleaseWithOnlyANameIsStillReadable()
    {
        var release = UpdateService.ParseRelease("""{ "name": "1.3.0" }""");

        release.ShouldNotBeNull();
        release.Tag.ShouldBe("1.3.0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("<html><body>Sign in to your network</body></html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    public void AnythingThatIsNotAReleaseReadsAsNothingRatherThanThrowing(string? body)
    {
        // The captive-portal case is the one that actually happens: a hotel
        // network answers every request with a login page, and an app that
        // assumed JSON would crash on a perfectly ordinary Tuesday.
        UpdateService.ParseRelease(body).ShouldBeNull();
    }

    [Fact]
    public async Task ANewerReleaseIsReportedAsAvailable()
    {
        var verdict = await Returning("""{ "tag_name": "v999.0.0", "html_url": "https://example.invalid/x" }""")
            .CheckAsync(TestContext.Current.CancellationToken);

        verdict.Outcome.ShouldBe(UpdateOutcome.Available);
        verdict.Url.ShouldBe("https://example.invalid/x");
    }

    [Fact]
    public async Task ATwoYearOldReleaseIsNotAnUpdate()
    {
        var verdict = await Returning("""{ "tag_name": "v0.0.1" }""").CheckAsync(TestContext.Current.CancellationToken);

        verdict.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task BeingOfflineIsAnUnansweredQuestionAndNotACrash()
    {
        var service = new UpdateService(_ => throw new HttpRequestException("no such host"));

        var verdict = await service.CheckAsync(TestContext.Current.CancellationToken);

        verdict.Outcome.ShouldBe(UpdateOutcome.Unknown);
    }

    [Fact]
    public async Task ATimeoutIsSwallowedTheSameWay()
    {
        var service = new UpdateService(_ => throw new TaskCanceledException());

        (await service.CheckAsync(TestContext.Current.CancellationToken)).Outcome.ShouldBe(UpdateOutcome.Unknown);
    }

    [Fact]
    public async Task ARefusedRequestIsSwallowedTheSameWay()
    {
        // What a rate limit or a proxy that blocks the host looks like from here.
        var service = new UpdateService(_ => Task.FromResult<string?>(null));

        (await service.CheckAsync(TestContext.Current.CancellationToken)).Outcome.ShouldBe(UpdateOutcome.Unknown);
    }

    [Fact]
    public async Task ACancelledCheckDoesNotEscape()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = new UpdateService(ct => Task.FromResult<string?>(null).WaitAsync(ct));

        (await service.CheckAsync(cts.Token)).Outcome.ShouldBe(UpdateOutcome.Unknown);
    }

    // ---- the files attached to a release -------------------------------------

    /// <summary>The asset shape GitHub really returns, verified against a live response.</summary>
    private const string WithAssets = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/o/r/releases/tag/v1.2.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "Mullion.exe",
              "state": "uploaded",
              "size": 67108864,
              "content_type": "application/octet-stream",
              "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/Mullion.exe"
            },
            {
              "name": "Mullion.exe.sha256",
              "state": "uploaded",
              "size": 78,
              "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/Mullion.exe.sha256"
            }
          ]
        }
        """;

    [Fact]
    public void TheAttachedFilesAreRead()
    {
        var release = UpdateService.ParseRelease(WithAssets);

        release.ShouldNotBeNull();
        release.Assets.ShouldNotBeNull();
        release.Assets.Count.ShouldBe(2);

        var exe = UpdateAssets.Executable(release.Assets);
        exe.ShouldNotBeNull();
        exe.Size.ShouldBe(67_108_864);
        exe.Url.ShouldBe("https://github.com/o/r/releases/download/v1.2.0/Mullion.exe");

        UpdateAssets.Checksum(release.Assets).ShouldNotBeNull();
    }

    [Fact]
    public void AnAssetStillUploadingIsSkipped()
    {
        // GitHub lists an asset from the moment its upload STARTS. Downloading
        // one of those gets a truncated executable - caught by the checksum,
        // but only after 60-odd MB and a progress bar that meant nothing.
        var json = """
            {
              "tag_name": "v1.2.0",
              "assets": [
                { "name": "Mullion.exe", "state": "starter", "browser_download_url": "https://example.invalid/x" }
              ]
            }
            """;

        UpdateService.ParseRelease(json)!.Assets.ShouldBeEmpty();
    }

    [Fact]
    public void AnAssetWithNoLinkIsSkipped()
    {
        var json = """{ "tag_name": "v1.2.0", "assets": [ { "name": "Mullion.exe" } ] }""";

        UpdateService.ParseRelease(json)!.Assets.ShouldBeEmpty();
    }

    [Fact]
    public void AReleaseWithNoAssetsArrayIsStillARelease()
    {
        // Every other field is readable, so the update can still be announced -
        // it just cannot be installed for you.
        var release = UpdateService.ParseRelease(
            """{ "tag_name": "v1.2.0", "html_url": "https://example.invalid/x" }""");

        release.ShouldNotBeNull();
        release.Tag.ShouldBe("v1.2.0");
        release.Assets.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAvailableUpdateCarriesTheReleaseSoItCanBeInstalled()
    {
        // The verdict has to bring the assets with it, or acting on it means
        // fetching the feed a second time.
        var verdict = await Returning(WithAssets).CheckAsync(TestContext.Current.CancellationToken);

        verdict.Outcome.ShouldBe(UpdateOutcome.Available);
        verdict.Release.ShouldNotBeNull();
        UpdateAssets.Executable(verdict.Release.Assets).ShouldNotBeNull();
    }

    [Fact]
    public void TheBuildKnowsWhereToLook()
    {
        // The default lives in the csproj, so a build that lost it would stop
        // checking silently - the exact failure the default exists to prevent.
        UpdateService.IsAvailable.ShouldBeTrue();
        UpdateService.FeedUrl.ShouldNotBeNullOrWhiteSpace();
        UpdateService.PageUrl.ShouldNotBeNullOrWhiteSpace();
    }
}
