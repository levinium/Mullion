using Mullion.App.ViewModels;

namespace Mullion.App.Services;

public sealed record WizardSnapshot(
    MonitorDiagramViewModel Detected,
    string TopologySummary,
    IReadOnlyList<LayoutChoiceViewModel> Choices,
    IReadOnlyList<ConflictViewModel> Conflicts);

public interface IWizardHost
{
    WizardSnapshot GetWizardSnapshot();

    /// <summary>Apply a layout live so the choice can be felt, not just seen.</summary>
    void PreviewLayout(string candidateId);

    void CommitLayout(string candidateId);

    void CloseWizard();
}

public sealed class DesignWizardHost : IWizardHost
{
    private readonly DesignAppHost _inner = new();

    public WizardSnapshot GetWizardSnapshot()
    {
        var snapshot = _inner.GetSnapshot();

        return new WizardSnapshot(
            snapshot.Diagram,
            snapshot.TopologySummary,
            [
                new LayoutChoiceViewModel
                {
                    Id = "recommended",
                    Name = "Recommended",
                    Rationale = "A 3.6:1 display, so a 16:9 centre with side columns.",
                    ZoneCount = 9,
                    KeySummary = "Q W E / A S D / Z X C",
                    Preview = snapshot.Diagram,
                    IsSelected = true,
                },
            ],
            []);
    }

    public void PreviewLayout(string candidateId) { }

    public void CommitLayout(string candidateId) { }

    public void CloseWizard() { }
}
