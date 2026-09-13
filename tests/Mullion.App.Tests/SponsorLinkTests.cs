using System.Globalization;
using Mullion.App.Services;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// The link behind "Support Mullion".
/// <para>
/// Two things are being protected. One is the repository: a funding handle
/// identifies a person, and a fork built from a clean checkout must not
/// solicit donations to whoever wrote the code - so with nothing configured
/// there is no button at all. The other is honesty: a picker is only offered
/// where the destination actually accepts the amount, because choosing $5 and
/// landing somewhere that never heard of it is worse than not being asked.
/// </para>
/// </summary>
public class SponsorLinkTests
{
    private const string WithAmount = "https://example.test/sponsors/someone?amount={amount}";
    private const string WithoutAmount = "https://example.test/sponsors/someone";

    [Fact]
    public void ACleanCheckoutOffersNothing()
    {
        // The repository ships no destination, so this assertion is what stops
        // a fork quietly asking for money on someone else's behalf. It is
        // expected to fail on a release build, which is published with one set.
        SponsorLink.Template.ShouldBeNull();
        SponsorLink.IsOffered.ShouldBeFalse();
    }

    [Fact]
    public void ADestinationThatTakesAnAmountGetsThePicker()
    {
        SponsorLink.CarriesAmount(WithAmount).ShouldBeTrue();
    }

    [Fact]
    public void ADestinationThatDoesNotIsAskedForNothing()
    {
        SponsorLink.CarriesAmount(WithoutAmount).ShouldBeFalse();
        SponsorLink.CarriesAmount(null).ShouldBeFalse();
    }

    [Fact]
    public void TheChosenAmountReachesTheLink()
    {
        SponsorLink.For(WithAmount, 5m).ShouldBe("https://example.test/sponsors/someone?amount=5");
    }

    [Fact]
    public void ADestinationWithoutAPlaceholderIsOpenedUntouched()
    {
        SponsorLink.For(WithoutAmount, 5m).ShouldBe(WithoutAmount);
    }

    [Fact]
    public void AWholeNumberCarriesNoTrailingZeros()
    {
        SponsorLink.Format(5m).ShouldBe("5");
        SponsorLink.Format(25m).ShouldBe("25");
    }

    [Fact]
    public void CentsSurviveWhenThereAreAny()
    {
        SponsorLink.Format(7.5m).ShouldBe("7.50");
    }

    [Fact]
    public void TheAmountIsWrittenTheWayAUrlExpects()
    {
        // The failure being guarded against: a decimal rendered under a culture
        // that writes "5,00" produces a URL the destination reads as a
        // different number, or as nothing at all - and the machine that would
        // show it is not the one this was written on.
        //
        // It cannot be provoked here. The app is built with
        // InvariantGlobalization, so no other culture can even be constructed;
        // attempting one throws. That is the real protection, and it is a build
        // setting rather than anything this file can exercise. Format asks for
        // the invariant culture explicitly anyway, so the separator survives
        // that setting being relaxed for localisation later - which is the
        // change that would otherwise reintroduce this silently.
        Should.Throw<CultureNotFoundException>(() => new CultureInfo("de-DE"));

        SponsorLink.Format(7.5m).ShouldBe("7.50");
        SponsorLink.For(WithAmount, 7.5m).ShouldEndWith("amount=7.50");
    }

    [Fact]
    public void TheDefaultIsOneOfTheOfferedAmounts()
    {
        // Otherwise the dialog opens with nothing selected, or with a selection
        // that matches no button.
        SponsorLink.Suggested.ShouldContain(SponsorLink.Default);
    }

    [Fact]
    public void TheSuggestedAmountsAreSensibleAndInOrder()
    {
        SponsorLink.Suggested.ShouldAllBe(a => a > 0);
        SponsorLink.Suggested.ShouldBe(SponsorLink.Suggested.OrderBy(a => a).ToList());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void AnUnusableAmountIsRefused(double amount)
    {
        SponsorLink.IsUsable((decimal)amount).ShouldBeFalse();
    }

    [Theory]
    [InlineData(7.5)]
    [InlineData(0.5)]
    [InlineData(24.99)]
    public void CentsAreRefusedBecauseTheDestinationDropsThem(double amount)
    {
        // Checked against the live sponsor page rather than assumed: its amount
        // box is pattern="[0-9]*" and handed 7.50 it renders 7, silently. A
        // dialog that accepted cents would name one number on its button and
        // open a page offering another, which is the one thing it must not do.
        SponsorLink.IsUsable((decimal)amount).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(1000)]
    public void AUsableAmountIsAccepted(double amount)
    {
        SponsorLink.IsUsable((decimal)amount).ShouldBeTrue();
    }

    [Fact]
    public void TheButtonSaysWhatWillBeSent()
    {
        SponsorLink.Describe(5m).ShouldBe("$5");
        SponsorLink.Describe(7.5m).ShouldBe("$7.50");
    }
}
