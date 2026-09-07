using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mullion.App.Services;
using Mullion.Core.Hotkeys;

namespace Mullion.App.ViewModels;

/// <summary>
/// The monitor diagram plus the controls that change it.
/// <para>
/// One view model for both windows. The editing began in settings, which meant
/// opening a second window to reshape the zones you were already looking at on
/// the first, and put Undo a page away from the thing it undoes. The main window
/// now offers the same editor behind an Edit button, and the controls sit
/// against the diagram in both.
/// </para>
/// <para>
/// Read-only until <see cref="IsEditing"/>. A diagram that is always editable
/// turns every stray click on a zone into a rebind prompt, and the main window
/// is somewhere people glance at rather than work in.
/// </para>
/// </summary>
public sealed partial class ZoneEditorViewModel : ObservableObject
{
    private readonly IZoneEditingHost _host;

    public ZoneEditorViewModel(IZoneEditingHost host)
    {
        _host = host;
        Refresh();
    }

    [ObservableProperty]
    private MonitorDiagramViewModel _diagram = new();

    [ObservableProperty]
    private bool _isEditing;

    /// <summary>What just happened, or what is being waited for. Null when idle.</summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>Where the diagram is shown but editing is not on offer.</summary>
    [ObservableProperty]
    private bool _canEdit = true;

    public bool SnapSplits
    {
        get => _host.SnapSplits;
        set
        {
            if (_host.SnapSplits == value) return;

            _host.SetSnapSplits(value);
            OnPropertyChanged();

            // The diagram works out its snap positions when it is built, so it
            // has to be rebuilt for the change to reach a drag.
            Refresh();
        }
    }

    public bool CanUndo => IsEditing && _host.CanUndoZones;

    public bool CanRedo => IsEditing && _host.CanRedoZones;

    public bool CanResetZones => IsEditing && _host.HasCustomZones;

    [RelayCommand]
    private void ToggleEdit()
    {
        IsEditing = !IsEditing;

        // A rebind left listening would swallow the next key pressed anywhere.
        if (!IsEditing) CancelCapture();

        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _host.UndoZones();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _host.RedoZones();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanResetZones))]
    private void ResetZones()
    {
        _host.ResetAllOverrides();
        Message = "Zones reset. Undo brings them back.";
        Refresh();
    }

    [RelayCommand]
    private void ResetKeys()
    {
        _host.ResetLayout();
        Message = "Keys reset to their derived positions.";
        Refresh();
    }

    /// <summary>
    /// Rebuild the diagram from the host, wired for editing or not.
    /// <para>
    /// Rebuilt rather than mutated because the layout is regenerated from the
    /// overrides after every change: the zones on screen are new objects, and
    /// the old ones describe a layout that no longer exists.
    /// </para>
    /// </summary>
    public void Refresh()
    {
        Diagram = IsEditing
            ? _host.BuildInteractiveDiagram(BeginRebindAt, ApplyWeights, ApplyZoneCount)
            : _host.BuildInteractiveDiagram(null, null, null);

        if (Capturing is not null) Diagram.SetCapturing(Capturing);

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanResetZones));
        OnPropertyChanged(nameof(SnapSplits));

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        ResetZonesCommand.NotifyCanExecuteChanged();
    }

    private void ApplyWeights(string slot, IReadOnlyList<double> weights)
    {
        _host.SetDisplayWeights(slot, weights);
        Refresh();
    }

    private void ApplyZoneCount(string slot, int count)
    {
        _host.SetDisplayColumns(slot, count);
        Refresh();
    }

    /// <summary>
    /// Which zone is waiting for a key, if any. Watched by the bindings list in
    /// settings so its matching row lights up too.
    /// </summary>
    public GridPos? Capturing { get; private set; }

    /// <summary>Raised when <see cref="Capturing"/> changes, with the old and new cells.</summary>
    public event Action<GridPos?, GridPos?>? CapturingChanged;

    /// <summary>
    /// Start listening for the key to give this zone. Public because the
    /// bindings list in settings offers the same thing as a row button, and it
    /// has to come through here: the editor owns the diagram, and a capture that
    /// does not light up the zone it is waiting on says nothing.
    /// </summary>
    public void BeginRebindAt(GridPos position)
    {
        // Clicking the same zone again cancels, so a capture is never a trap
        // with no way out - especially as the next key pressed anywhere is the
        // one it will take.
        if (Capturing == position)
        {
            CancelCapture();
            return;
        }

        CancelCapture();

        SetCapturing(position);
        Message = "Hold the modifier and press the key you want for this zone. Click it again to cancel.";

        _host.BeginRebind(position.Row, position.Col, result =>
        {
            SetCapturing(null);
            Message = result.Message;

            if (result.Success) Refresh();
        });
    }

    public void CancelCapture()
    {
        if (Capturing is null) return;

        SetCapturing(null);
        _host.CancelRebind();
        Message = null;
    }

    private void SetCapturing(GridPos? position)
    {
        var was = Capturing;
        Capturing = position;

        Diagram.SetCapturing(position);
        CapturingChanged?.Invoke(was, position);
    }

}
