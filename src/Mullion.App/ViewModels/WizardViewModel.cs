using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;

namespace Mullion.App.ViewModels;

public sealed partial class LayoutChoiceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Rationale { get; init; }
    public required int ZoneCount { get; init; }
    public required string KeySummary { get; init; }
    public required MonitorDiagramViewModel Preview { get; init; }
}

public sealed partial class WizardViewModel : ObservableObject
{
    private readonly IWizardHost _host;

    [ObservableProperty]
    private int _step;

    [ObservableProperty]
    private MonitorDiagramViewModel _detected = new();

    [ObservableProperty]
    private string _topologySummary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<LayoutChoiceViewModel> _choices = [];

    [ObservableProperty]
    private LayoutChoiceViewModel? _selectedChoice;

    [ObservableProperty]
    private IReadOnlyList<ConflictViewModel> _conflicts = [];

    public WizardViewModel() : this(new DesignWizardHost()) { }

    public WizardViewModel(IWizardHost host)
    {
        _host = host;
        Reload();
    }

    public int StepCount => 3;

    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => Step == StepCount - 1;
    public bool HasConflicts => Conflicts.Count > 0;

    public string StepTitle => Step switch
    {
        0 => "Your displays",
        1 => "Choose a layout",
        _ => "You're set up",
    };

    public string StepBlurb => Step switch
    {
        0 => "This is how Mullion sees your desk. Check the arrangement matches reality — "
             + "if it doesn't, fix it in Windows display settings and rescan.",
        1 => "Each option is drawn to scale on your actual displays. The highlighted key is the "
             + "main target for that column; the smaller keys above and below are its halves.",
        _ => "Hotkeys are active. Mullion keeps running in the tray — open it any time to change the layout.",
    };

    [RelayCommand]
    private void Next()
    {
        if (Step < StepCount - 1) Step++;
        RaiseStepProperties();
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0) Step--;
        RaiseStepProperties();
    }

    [RelayCommand]
    private void Rescan() => Reload();

    [RelayCommand]
    private void Select(LayoutChoiceViewModel? choice)
    {
        if (choice is null) return;

        foreach (var c in Choices) c.IsSelected = ReferenceEquals(c, choice);

        SelectedChoice = choice;
        _host.PreviewLayout(choice.Id);
    }

    [RelayCommand]
    private void Finish()
    {
        if (SelectedChoice is not null) _host.CommitLayout(SelectedChoice.Id);
        _host.CloseWizard();
    }

    private void Reload()
    {
        var snapshot = _host.GetWizardSnapshot();

        Detected = snapshot.Detected;
        TopologySummary = snapshot.TopologySummary;
        Conflicts = snapshot.Conflicts;
        Choices = snapshot.Choices;
        SelectedChoice = Choices.FirstOrDefault();

        foreach (var c in Choices) c.IsSelected = ReferenceEquals(c, SelectedChoice);

        OnPropertyChanged(nameof(HasConflicts));
        RaiseStepProperties();
    }

    private void RaiseStepProperties()
    {
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepBlurb));
    }
}
