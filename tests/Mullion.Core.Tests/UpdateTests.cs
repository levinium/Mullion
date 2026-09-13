using Mullion.Core.Updates;
using Shouldly;
using Xunit;

namespace Mullion.Core.Tests;

/// <summary>
/// Reading a version, and deciding whether to mention it.
/// <para>
/// The whole feature reduces to one comparison, and the comparison has one
/// failure mode that matters: getting it wrong tells everybody they are up to
/// date and then never says anything again. Nobody reports that - there is
/// nothing to see - so it has to be caught here.
/// </para>
/// </summary>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("v1.0.0", 1, 0, 0)]
    [InlineData("V2.10.3", 2, 10, 3)]
    [InlineData("  1.2.3  ", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.0.0+abc123", 1, 0, 0)]
    public void AVersionIsReadOutOfTheNameATagCarries(string text, int major, int minor, int patch)
    {
        ReleaseVersion.TryParse(text, out var v).ShouldBeTrue();

        v.Major.ShouldBe(major);
        v.Minor.ShouldBe(minor);
        v.Patch.ShouldBe(patch);
        v.IsPreRelease.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("nightly")]
    [InlineData("1.2.3.4")]
    [InlineData("1.-2.3")]
    [InlineData("1.x.3")]
    [InlineData("1.0.0-")]
    public void SomethingThatIsNotAVersionIsRefusedRatherThanGuessedAt(string? text)
    {
        ReleaseVersion.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void APreReleaseSuffixIsKept()
    {
        ReleaseVersion.TryParse("v1.1.0-rc.1", out var v).ShouldBeTrue();

        v.PreRelease.ShouldBe("rc.1");
        v.IsPreRelease.ShouldBeTrue();
        v.ToString().ShouldBe("1.1.0-rc.1");
    }

    [Fact]
    public void TenIsNewerThanNineRatherThanEarlierInTheAlphabet()
    {
        // The reason this type exists. Ordered as text, "1.10.0" sorts BEFORE
        // "1.9.0", so everyone on 1.9.0 would be told they were current and
        // would never hear about a release again.
        ReleaseVersion.TryParse("1.10.0", out var ten).ShouldBeTrue();
        ReleaseVersion.TryParse("1.9.0", out var nine).ShouldBeTrue();

        ten.IsNewerThan(nine).ShouldBeTrue();
        nine.IsNewerThan(ten).ShouldBeFalse();
    }

    [Theory]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.2")]
    [InlineData("1.0.0-rc.1.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    public void TheNewerOfTwoVersionsIsTheOneThatCameOutLater(string newer, string older)
    {
        ReleaseVersion.TryParse(newer, out var a).ShouldBeTrue();
        ReleaseVersion.TryParse(older, out var b).ShouldBeTrue();

        a.IsNewerThan(b).ShouldBeTrue($"{newer} should be newer than {older}");
        b.IsNewerThan(a).ShouldBeFalse($"{older} should not be newer than {newer}");
    }

    [Fact]
    public void AFinishedReleaseBeatsItsOwnReleaseCandidate()
    {
        // Backwards, this offers somebody on 1.1.0 a "newer" 1.1.0-rc1.
        ReleaseVersion.TryParse("1.1.0", out var final).ShouldBeTrue();
        ReleaseVersion.TryParse("1.1.0-rc1", out var candidate).ShouldBeTrue();

        final.IsNewerThan(candidate).ShouldBeTrue();
        candidate.IsNewerThan(final).ShouldBeFalse();
    }

    [Fact]
    public void TheSameVersionIsNotNewerThanItself()
    {
        ReleaseVersion.TryParse("1.0.0", out var a).ShouldBeTrue();
        ReleaseVersion.TryParse("v1.0.0", out var b).ShouldBeTrue();

        a.IsNewerThan(b).ShouldBeFalse();
        b.IsNewerThan(a).ShouldBeFalse();
        a.ShouldBe(b);
    }

    [Fact]
    public void BuildMetadataIsNotPartOfTheVersion()
    {
        // Two builds of one release are one release. If the commit counted, every
        // rebuild would look like an update.
        ReleaseVersion.TryParse("1.0.0+aaaaaaa", out var a).ShouldBeTrue();
        ReleaseVersion.TryParse("1.0.0+bbbbbbb", out var b).ShouldBeTrue();

        a.ShouldBe(b);
    }
}

/// <summary>Whether a release that was found is one worth interrupting anyone about.</summary>
public class UpdateDecisionTests
{
    private static ReleaseInfo Release(string tag, bool draft = false, bool pre = false) =>
        new(tag, $"https://example.invalid/{tag}", draft, pre);

    [Fact]
    public void ANewerReleaseIsOffered()
    {
        var verdict = UpdateDecision.For("1.0.0", Release("v1.1.0"));

        verdict.Outcome.ShouldBe(UpdateOutcome.Available);
        verdict.IsAvailable.ShouldBeTrue();
        verdict.Version.ToString().ShouldBe("1.1.0");
        verdict.Url.ShouldBe("https://example.invalid/v1.1.0");
    }

    [Fact]
    public void TheSameReleaseIsNotOffered()
    {
        UpdateDecision.For("1.0.0", Release("v1.0.0"))
            .Outcome.ShouldBe(UpdateOutcome.UpToDate);
    }

    [Fact]
    public void ABuildNewerThanAnythingPublishedIsUpToDate()
    {
        // Running from source between releases. Offering a "newer" 1.0.0 to
        // somebody already on 1.1.0 would be a downgrade wearing the wrong label.
        UpdateDecision.For("1.1.0", Release("v1.0.0"))
            .Outcome.ShouldBe(UpdateOutcome.UpToDate);
    }

    [Fact]
    public void ADraftIsNotAPublishedRelease()
    {
        UpdateDecision.For("1.0.0", Release("v2.0.0", draft: true))
            .IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void AReleaseMarkedPreReleaseIsNotOffered()
    {
        UpdateDecision.For("1.0.0", Release("v2.0.0", pre: true))
            .IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void ATagThatReadsAsAPreReleaseIsRefusedEvenUnlabelled()
    {
        // The flag and the tag can disagree; the tag is the one that cannot be
        // forgotten to be set.
        UpdateDecision.For("1.0.0", Release("v2.0.0-rc.1"))
            .IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void NothingFetchedMeansNothingIsKnown()
    {
        var verdict = UpdateDecision.For("1.0.0", null);

        verdict.Outcome.ShouldBe(UpdateOutcome.Unknown);
        verdict.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void AnUnreadableTagIsNotTreatedAsUpToDate()
    {
        // "Unknown" and "up to date" look the same to a user until the day a
        // release is missed. They are different answers and are kept apart.
        UpdateDecision.For("1.0.0", Release("nightly-build"))
            .Outcome.ShouldBe(UpdateOutcome.Unknown);
    }

    [Fact]
    public void AnUnreadableRunningVersionIsNotTreatedAsUpToDate()
    {
        UpdateDecision.For("dev", Release("v1.1.0"))
            .Outcome.ShouldBe(UpdateOutcome.Unknown);
    }

    [Fact]
    public void APatchReleaseCountsAsAnUpdate()
    {
        UpdateDecision.For("1.0.0", Release("v1.0.1"))
            .IsAvailable.ShouldBeTrue();
    }
}

/// <summary>How often the app is allowed to go and look.</summary>
public class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AMachineThatHasNeverCheckedIsDue()
    {
        UpdateSchedule.IsDue(null, Now).ShouldBeTrue();
    }

    [Fact]
    public void CheckingTwiceInAnHourDoesNotHappen()
    {
        // Signing in and out, or restarting after a settings change, must not
        // turn into a request each time.
        UpdateSchedule.IsDue(Now.AddHours(-1), Now).ShouldBeFalse();
    }

    [Fact]
    public void ADayLaterItIsDueAgain()
    {
        UpdateSchedule.IsDue(Now.AddDays(-1), Now).ShouldBeTrue();
        UpdateSchedule.IsDue(Now.AddDays(-30), Now).ShouldBeTrue();
    }

    [Fact]
    public void TheBoundaryCounts()
    {
        UpdateSchedule.IsDue(Now - UpdateSchedule.Interval, Now).ShouldBeTrue();
        UpdateSchedule.IsDue(Now - UpdateSchedule.Interval + TimeSpan.FromSeconds(1), Now).ShouldBeFalse();
    }

    [Fact]
    public void ATimeInTheFutureIsDueRatherThanNeverDueAgain()
    {
        // What a clock correction leaves behind. Compared naively this reads as
        // "checked recently" and stays that way forever, so the check quietly
        // stops running on exactly the machines nobody is watching.
        UpdateSchedule.IsDue(Now.AddYears(1), Now).ShouldBeTrue();
    }
}
