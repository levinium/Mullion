using Mullion.Core.Updates;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// Choosing what to download, and deciding whether it arrived intact.
/// <para>
/// A self-update replaces the app with a file off the internet, so the two
/// questions it has to answer are "which file" and "is this really it". Both
/// are string handling, and both fail in ways a working network never shows.
/// </para>
/// </summary>
public class UpdateAssetTests
{
    private static ReleaseAsset Asset(string name, long size = 1000) =>
        new(name, $"https://example.invalid/{name}", size);

    [Fact]
    public void TheExecutableIsFoundAmongTheRelease()
    {
        IReadOnlyList<ReleaseAsset> assets =
        [
            Asset("Mullion.exe.sha256", 80),
            Asset("Mullion.exe", 67_000_000),
        ];

        UpdateAssets.Executable(assets)!.Name.ShouldBe("Mullion.exe");
        UpdateAssets.Executable(assets)!.Size.ShouldBe(67_000_000);
    }

    [Fact]
    public void TheChecksumFileIsNotMistakenForTheExecutable()
    {
        // The bug this exists to prevent. "Mullion.exe.sha256" CONTAINS
        // "Mullion.exe", so a substring match picks the 80-byte text file,
        // renames it over the app, and leaves the machine with a Mullion.exe
        // that is not a program. Whole-name matching is what stops that.
        IReadOnlyList<ReleaseAsset> assets = [Asset("Mullion.exe.sha256", 80)];

        UpdateAssets.Executable(assets).ShouldBeNull();
        UpdateAssets.Checksum(assets).ShouldNotBeNull();
    }

    [Fact]
    public void ChoosingIsCaseInsensitive()
    {
        IReadOnlyList<ReleaseAsset> assets = [Asset("mullion.EXE")];

        UpdateAssets.Executable(assets).ShouldNotBeNull();
    }

    [Fact]
    public void AReleaseeWithNoAssetsOffersNothing()
    {
        UpdateAssets.Executable(null).ShouldBeNull();
        UpdateAssets.Executable([]).ShouldBeNull();
        UpdateAssets.Checksum(null).ShouldBeNull();
    }

    [Fact]
    public void ASourceOnlyReleaseIsNotInstallable()
    {
        // GitHub attaches source tarballs to every release whether or not a
        // build was uploaded. Neither is something to run.
        IReadOnlyList<ReleaseAsset> assets =
        [
            Asset("Source code (zip)"),
            Asset("Source code (tar.gz)"),
        ];

        UpdateAssets.Executable(assets).ShouldBeNull();
    }

    // ---- reading the published checksum -------------------------------------

    private const string RealHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void TheHashIsReadFromWhatTheWorkflowWrites()
    {
        // Exactly the line tools/release.yml produces: hash, two spaces, name.
        UpdateAssets.TryReadChecksum($"{RealHash}  Mullion.exe", out var hash).ShouldBeTrue();

        hash.ShouldBe(RealHash);
    }

    [Theory]
    [InlineData("{0}  Mullion.exe\r\n")]
    [InlineData("{0} *Mullion.exe\n")]
    [InlineData("  {0}  Mullion.exe  ")]
    [InlineData("\uFEFF{0}  Mullion.exe")]
    [InlineData("{0}")]
    public void TheUsualVariationsStillRead(string template)
    {
        // CRLF from PowerShell, the "*" binary marker from sha256sum, a BOM
        // from Out-File, and a bare hash with no name at all.
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, RealHash);

        UpdateAssets.TryReadChecksum(text, out var hash).ShouldBeTrue(template);
        hash.ShouldBe(RealHash);
    }

    [Fact]
    public void AnUpperCaseHashIsNormalized()
    {
        UpdateAssets.TryReadChecksum(RealHash.ToUpperInvariant() + "  Mullion.exe", out var hash)
            .ShouldBeTrue();

        hash.ShouldBe(RealHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a hash at all")]
    [InlineData("<html>Sign in to your network</html>")]
    [InlineData("9f86d081")]
    [InlineData("zzzzd081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void AnythingThatIsNotAHashIsRefused(string? text)
    {
        // Refusing means the update does not install. That is the safe
        // direction: a checksum that cannot be read is a check that did not
        // happen, and this is the only thing standing between a download and
        // the app being replaced by it.
        UpdateAssets.TryReadChecksum(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void AHashTooLongIsRefused()
    {
        UpdateAssets.TryReadChecksum(RealHash + "ab  Mullion.exe", out _).ShouldBeFalse();
    }

    // ---- comparing -----------------------------------------------------------

    [Fact]
    public void AMatchingHashMatchesWhateverCaseEitherSideUses()
    {
        // Get-FileHash returns upper case and the published file is lower. An
        // ordinal comparison would reject every genuine update while looking
        // exactly like a tampered download.
        UpdateAssets.Matches(RealHash, RealHash.ToUpperInvariant()).ShouldBeTrue();
        UpdateAssets.Matches(RealHash.ToUpperInvariant(), RealHash).ShouldBeTrue();
    }

    [Fact]
    public void ADifferentHashDoesNotMatch()
    {
        UpdateAssets.Matches(RealHash, RealHash.Replace('9', 'a')).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(null, "abc")]
    [InlineData("abc", null)]
    [InlineData("   ", "   ")]
    public void NothingMatchesNothing(string? a, string? b)
    {
        // Two absent hashes are not agreement. Left as equality, a release with
        // no checksum and a download that produced none would "verify".
        UpdateAssets.Matches(a, b).ShouldBeFalse();
    }

    [Fact]
    public void AComputedHashRendersTheWayTheFileWritesIt()
    {
        var digest = System.Security.Cryptography.SHA256.HashData("abc"u8.ToArray());

        UpdateAssets.Format(digest)
            .ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }
}

/// <summary>Where the files go, and what a crash leaves behind.</summary>
public class UpdatePlanTests
{
    private const string Exe = @"C:\Apps\Mullion\Mullion.exe";

    [Fact]
    public void EverythingHappensBesideTheExecutable()
    {
        // A rename is atomic within a volume; a copy across one is not. Staging
        // in the temp directory would make the critical step a 64MB copy that
        // can fail halfway with the running exe already moved aside.
        var plan = UpdatePlan.For(Exe);

        Path.GetDirectoryName(plan.Staged).ShouldBe(@"C:\Apps\Mullion");
        Path.GetDirectoryName(plan.Backup).ShouldBe(@"C:\Apps\Mullion");
        plan.Directory.ShouldBe(@"C:\Apps\Mullion");
    }

    [Fact]
    public void TheThreePathsAreAllDifferent()
    {
        var plan = UpdatePlan.For(Exe);

        new[] { plan.Current, plan.Staged, plan.Backup }.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void TheNewExecutableTakesTheNameTheOldOneHad()
    {
        // Which is the point: auto-start records a path, shortcuts record a
        // path, and an update that changed it would break both.
        UpdatePlan.For(Exe).Current.ShouldBe(Exe);
    }

    [Fact]
    public void TheNamesAreFixedSoALaterLaunchRecognizesThem()
    {
        // A random temp name would leave 64MB of litter after a crash that
        // nothing ever attributes to Mullion.
        var a = UpdatePlan.For(Exe);
        var b = UpdatePlan.For(Exe);

        a.ShouldBe(b);
    }

    [Fact]
    public void BothLeftoversAreNamedForCleanup()
    {
        var plan = UpdatePlan.For(Exe);

        plan.Leftovers().ShouldBe([plan.Backup, plan.Staged], ignoreOrder: true);
    }

    [Fact]
    public void ThePlanRefusesAnEmptyPath()
    {
        Should.Throw<ArgumentException>(() => UpdatePlan.For(""));
        Should.Throw<ArgumentException>(() => UpdatePlan.For(null!));
    }
}
