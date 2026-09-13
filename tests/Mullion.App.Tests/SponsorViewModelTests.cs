using Mullion.App.ViewModels;
using Shouldly;
using Xunit;

namespace Mullion.App.Tests;

/// <summary>
/// The ask, as behaviour rather than as pixels.
/// <para>
/// The thing worth protecting here is that the dialog never says one number and
/// sends another. Everything else about it is a judgement call; that is a lie.
/// </para>
/// </summary>
public class SponsorViewModelTests
{
    private const string TakesAmount = "https://example.test/s/someone?amount={amount}";
    private const string TakesNone = "https://example.test/s/someone";

    private static SponsorViewModel Model(string template = TakesAmount)
    {
        var model = new SponsorViewModel(template);
        model.OpenRequested = _ => { };
        model.CloseRequested = () => { };
        return model;
    }

    [Fact]
    public void ItOpensOnTheDefaultAmount()
    {
        // The quickest path through the dialog should be one click on a sum
        // nobody regrets, not a decision.
        var model = Model();

        model.Amount.ShouldBe(5m);
        model.CanSponsor.ShouldBeTrue();
        model.ActionLabel.ShouldBe("Sponsor $5");
    }

    [Fact]
    public void ChoosingAnAmountSelectsOnlyThatOne()
    {
        var model = Model();
        var ten = model.Amounts.Single(a => a.Value == 10m);

        model.ChooseCommand.Execute(ten);

        model.Amounts.Count(a => a.IsSelected).ShouldBe(1);
        model.Amount.ShouldBe(10m);
        model.ActionLabel.ShouldBe("Sponsor $10");
    }

    [Fact]
    public void ChoosingOtherLeavesNothingElseSelected()
    {
        var model = Model();

        model.ChooseOtherCommand.Execute(null);

        model.IsOther.ShouldBeTrue();
        model.Amounts.ShouldAllBe(a => !a.IsSelected);
    }

    [Fact]
    public void OtherWithNothingTypedCannotBeSent()
    {
        // Better a disabled button that says what is missing than a payment
        // page opened for an amount nobody entered.
        var model = Model();

        model.ChooseOtherCommand.Execute(null);

        model.Amount.ShouldBeNull();
        model.CanSponsor.ShouldBeFalse();
        model.ActionLabel.ShouldBe("Enter an amount");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("99999")]
    [InlineData("7.50")]
    public void ANonsenseOtherAmountCannotBeSent(string typed)
    {
        var model = Model();
        model.ChooseOtherCommand.Execute(null);
        model.OtherAmount = typed;

        model.CanSponsor.ShouldBeFalse();
    }

    [Fact]
    public void CentsSayWhyRatherThanJustRefusing()
    {
        // A disabled button and the word "amount" leaves someone re-reading a
        // field that looks like it ought to take 7.50. The destination truncates
        // it, so the dialog has to say so.
        var model = Model();
        model.ChooseOtherCommand.Execute(null);
        model.OtherAmount = "7.50";

        model.CanSponsor.ShouldBeFalse();
        model.ActionLabel.ShouldBe("Whole amounts only");
    }

    [Fact]
    public void ATypedAmountIsWhatGetsSent()
    {
        var model = Model();
        model.ChooseOtherCommand.Execute(null);
        model.OtherAmount = "12";

        model.Amount.ShouldBe(12m);
        model.ActionLabel.ShouldBe("Sponsor $12");
    }

    [Fact]
    public void TheLinkOpenedCarriesTheAmountOnTheButton()
    {
        // The one assertion that matters: what the button promises is what the
        // browser is handed.
        var model = Model();
        string? opened = null;
        model.OpenRequested = url => opened = url;

        model.ChooseCommand.Execute(model.Amounts.Single(a => a.Value == 25m));
        model.ActionLabel.ShouldBe("Sponsor $25");

        model.SponsorCommand.Execute(null);

        opened.ShouldBe("https://example.test/s/someone?amount=25");
    }

    [Fact]
    public void ADestinationThatTakesNoAmountIsNotAskedForOne()
    {
        // Offering a picker here would be a lie: whatever was chosen, the page
        // would never hear about it.
        var model = Model(TakesNone);
        string? opened = null;
        model.OpenRequested = url => opened = url;

        model.ShowAmounts.ShouldBeFalse();
        model.Amount.ShouldBeNull();
        model.CanSponsor.ShouldBeTrue();
        model.ActionLabel.ShouldBe("Open the sponsor page");

        model.SponsorCommand.Execute(null);

        opened.ShouldBe(TakesNone);
    }

    [Fact]
    public void SendingSomeoneOnwardClosesTheDialog()
    {
        var model = Model();
        var closed = false;
        model.CloseRequested = () => closed = true;

        model.SponsorCommand.Execute(null);

        closed.ShouldBeTrue();
    }

    [Fact]
    public void NotNowJustCloses()
    {
        var model = Model();
        var closed = false;
        var opened = false;
        model.CloseRequested = () => closed = true;
        model.OpenRequested = _ => opened = true;

        model.DismissCommand.Execute(null);

        closed.ShouldBeTrue();
        opened.ShouldBeFalse("declining must not open anything");
    }

    [Fact]
    public void ABuildWithNoDestinationOpensNothing()
    {
        // Belt and braces behind the hidden button: even reached somehow, a
        // build with nowhere to send anyone sends nobody anywhere.
        var model = new SponsorViewModel(string.Empty);
        var opened = false;
        model.OpenRequested = _ => opened = true;

        model.SponsorCommand.Execute(null);

        opened.ShouldBeFalse();
    }
}
