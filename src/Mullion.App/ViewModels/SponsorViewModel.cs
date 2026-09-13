using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;

namespace Mullion.App.ViewModels;

/// <summary>One of the offered amounts, and whether it is the one chosen.</summary>
public sealed partial class SponsorAmountViewModel : ObservableObject
{
    public required decimal Value { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    public string Label => SponsorLink.Describe(Value);
}

/// <summary>
/// The one place Mullion asks for anything.
/// <para>
/// Written to be easy to say no to. There is no countdown, nothing is
/// pre-ticked beyond the amount itself, and the dialog is reachable only by
/// pressing a heart nobody has to press - so the honest version of this is a
/// clear ask, a sensible default, and a door that is obviously open.
/// </para>
/// </summary>
public sealed partial class SponsorViewModel : ObservableObject
{
    /// <summary>Set by the window; the view model opens no browsers itself.</summary>
    public Action<string>? OpenRequested { get; set; }

    /// <summary>Set by the window, so choosing an amount can close it.</summary>
    public Action? CloseRequested { get; set; }

    private readonly string _template;

    public SponsorViewModel() : this(SponsorLink.Template ?? string.Empty) { }

    public SponsorViewModel(string template)
    {
        _template = template;

        Amounts =
        [
            .. SponsorLink.Suggested.Select(a => new SponsorAmountViewModel
            {
                Value = a,
                IsSelected = a == SponsorLink.Default,
            })
        ];

        foreach (var amount in Amounts)
            amount.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SponsorAmountViewModel.IsSelected)) Recalculate();
            };
    }

    public IReadOnlyList<SponsorAmountViewModel> Amounts { get; }

    /// <summary>
    /// Whether to offer a choice at all. Only where the destination takes the
    /// amount in its link: picking $5 and landing somewhere that never heard of
    /// it is worse than not being asked.
    /// </summary>
    public bool ShowAmounts => SponsorLink.CarriesAmount(_template);

    [ObservableProperty]
    private bool _isOther;

    [ObservableProperty]
    private string _otherAmount = string.Empty;

    /// <summary>The sum that will actually be sent, or null when it makes no sense.</summary>
    public decimal? Amount
    {
        get
        {
            if (!ShowAmounts) return null;

            if (IsOther)
            {
                return decimal.TryParse(
                    OtherAmount,
                    System.Globalization.NumberStyles.Currency,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var typed) && SponsorLink.IsUsable(typed)
                    ? typed
                    : null;
            }

            return Amounts.FirstOrDefault(a => a.IsSelected)?.Value;
        }
    }

    /// <summary>
    /// What the button says. Naming the sum means nobody has to look back up at
    /// the row of chips to check what they are about to be charged.
    /// </summary>
    public string ActionLabel =>
        !ShowAmounts ? "Open the sponsor page"
        : Amount is { } amount ? $"Sponsor {SponsorLink.Describe(amount)}"
        : HasCents ? "Whole amounts only"
        : "Enter an amount";

    /// <summary>
    /// Typed something with cents in it. Worth saying out loud rather than
    /// leaving a disabled button and the word "amount": the field looks like it
    /// ought to take 7.50, and being told why it does not beats guessing.
    /// </summary>
    private bool HasCents =>
        IsOther
        && decimal.TryParse(
            OtherAmount,
            System.Globalization.NumberStyles.Currency,
            System.Globalization.CultureInfo.InvariantCulture,
            out var typed)
        && typed > 0
        && typed != decimal.Truncate(typed);

    public bool CanSponsor => !ShowAmounts || Amount is not null;

    [RelayCommand]
    private void Choose(SponsorAmountViewModel? amount)
    {
        if (amount is null) return;

        IsOther = false;

        foreach (var each in Amounts) each.IsSelected = ReferenceEquals(each, amount);
    }

    [RelayCommand]
    private void ChooseOther()
    {
        IsOther = true;

        foreach (var each in Amounts) each.IsSelected = false;
    }

    [RelayCommand]
    private void Sponsor()
    {
        if (!CanSponsor || string.IsNullOrWhiteSpace(_template)) return;

        OpenRequested?.Invoke(
            Amount is { } amount ? SponsorLink.For(_template, amount) : _template);

        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Dismiss() => CloseRequested?.Invoke();

    partial void OnIsOtherChanged(bool value) => Recalculate();

    partial void OnOtherAmountChanged(string value) => Recalculate();

    private void Recalculate()
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(CanSponsor));
        SponsorCommand.NotifyCanExecuteChanged();
    }
}
