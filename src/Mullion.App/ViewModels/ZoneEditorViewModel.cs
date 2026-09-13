using Avalonia.Threading;
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

    /// <summary>
    /// What is being waited for, and what went wrong. Null when idle.
    /// <para>
    /// Sticky, and on a line of its own: these are the ones that have to be
    /// read and acted on - which key to press, why the last one was refused -
    /// and they are long enough to need the width.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>
    /// What just happened. Clears itself after a few seconds.
    /// <para>
    /// A confirmation is worth saying and not worth keeping. Sharing the sticky
    /// message's line, "Changes discarded." pushed the whole diagram down to
    /// announce that nothing had changed, and then stayed there. This one sits
    /// in room the toolbar has already claimed, so showing it moves nothing.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private string? _flash;

    /// <summary>
    /// How long a confirmation stays up. Long enough to read a short sentence
    /// twice, short enough that it is gone before it is furniture.
    /// </summary>
    private static readonly TimeSpan FlashLifetime = TimeSpan.FromSeconds(4);

    private DispatcherTimer? _flashTimer;

    /// <summary>Say something that does not need answering.</summary>
    private void ShowFlash(string text)
    {
        // The two are alternatives, never both: one line is showing at a time
        // and a stale instruction under a fresh confirmation reads as current.
        Message = null;
        Flash = text;

        // Built on first use rather than in the constructor. A view model is
        // constructed in tests that never start a dispatcher, and one that
        // demanded a timer to exist would not be constructible there at all.
        _flashTimer ??= new DispatcherTimer { Interval = FlashLifetime };

        if (_flashTimer.Tag is null)
        {
            _flashTimer.Tag = this;
            _flashTimer.Tick += (_, _) => DismissFlash();
        }

        // Restarted, not merely started: a second confirmation gets its own full
        // reading time rather than inheriting what was left of the first.
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    /// <summary>
    /// Take it down now. Bound to the message itself, so it can be dismissed by
    /// clicking rather than waited out.
    /// </summary>
    [RelayCommand]
    private void DismissFlash()
    {
        _flashTimer?.Stop();
        Flash = null;
    }

    /// <summary>Where the diagram is shown but editing is not on offer.</summary>
    [ObservableProperty]
    private bool _canEdit = true;

    public bool SnapSplits => _host.SnapSplits;

    [RelayCommand]
    private void ToggleSnap()
    {
        _host.SetSnapSplits(!_host.SnapSplits);

        // The diagram works out its snap positions when it is built, so it has
        // to be rebuilt for the change to reach a drag.
        Refresh();
    }

    public bool CanUndo => IsEditing && _host.CanUndoZones;

    public bool CanRedo => IsEditing && _host.CanRedoZones;

    public bool CanResetZones => IsEditing && _host.HasCustomZones;

    public bool CanResetKeys => IsEditing && _host.HasCustomKeys;

    /// <summary>
    /// The pencil, shown where editing is on offer and not already under way.
    /// </summary>
    public bool ShowEditButton => CanEdit && !IsEditing;

    /// <summary>The tick and the cross, which replace it for the session.</summary>
    public bool ShowSessionButtons => CanEdit && IsEditing;

    /// <summary>
    /// Whether ending the session has anything to end.
    /// <para>
    /// Only a diagram offering the pencil runs one. Without it there is no way
    /// to confirm or cancel, so changes are written as they are made: a
    /// provisional state nothing can resolve is just changes that never get
    /// saved.
    /// </para>
    /// </summary>
    private bool InSession => CanEdit;

    [RelayCommand]
    private void BeginEdit()
    {
        if (IsEditing) return;

        if (InSession) _host.BeginZoneEdit();

        IsEditing = true;
        Message = null;
        DismissFlash();
        Refresh();
    }

    [RelayCommand]
    private void ConfirmEdit() => EndEdit(keep: true);

    [RelayCommand]
    private void CancelEdit() => EndEdit(keep: false);

    private void EndEdit(bool keep)
    {
        if (!IsEditing) return;

        // Before anything else: a capture left armed takes the next key pressed
        // anywhere, and it must not outlive the session that started it.
        CancelCapture();

        if (InSession)
        {
            if (keep) _host.CommitZoneEdit();
            else _host.CancelZoneEdit();
        }

        IsEditing = false;
        Message = null;

        if (keep) DismissFlash();
        else ShowFlash("Changes discarded.");

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
        ShowFlash("Zones reset. Undo brings them back.");
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanResetKeys))]
    private void ResetKeys()
    {
        _host.ResetLayout();
        ShowFlash("Keys reset to their derived positions.");
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
            ? _host.BuildInteractiveDiagram(BeginRebindAt, ApplyWeights, ApplyZoneCount, FlipSubzoneAxis)
            : _host.BuildInteractiveDiagram(null, null, null, null);

        if (Capturing is not null) Diagram.SetCapturing(Capturing);

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(ShowEditButton));
        OnPropertyChanged(nameof(ShowSessionButtons));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanResetZones));
        OnPropertyChanged(nameof(CanResetKeys));
        OnPropertyChanged(nameof(SnapSplits));

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        ResetZonesCommand.NotifyCanExecuteChanged();
        ResetKeysCommand.NotifyCanExecuteChanged();
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

    private void FlipSubzoneAxis(string slot, int zone)
    {
        _host.FlipSubzoneAxis(slot, zone);
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
        DismissFlash();
        Message = "Hold the modifier and press the key you want for this zone. Click it again to cancel.";

        _host.BeginRebind(position.Row, position.Col, result =>
        {
            SetCapturing(null);

            Message = null;

            // Backing out says nothing: the user changed their mind, and there
            // is nothing to confirm or explain. A key that landed is a
            // confirmation; one that was refused has to stay up, because it is
            // asking for a different key.
            if (result.Canceled) DismissFlash();
            else if (result.Success) ShowFlash(result.Message);
            else Message = result.Message;

            if (result.Success) Refresh();
        });
    }

    public void CancelCapture()
    {
        if (Capturing is null) return;

        SetCapturing(null);
        _host.CancelRebind();
        Message = null;
        DismissFlash();
    }

    private void SetCapturing(GridPos? position)
    {
        var was = Capturing;
        Capturing = position;

        Diagram.SetCapturing(position);
        CapturingChanged?.Invoke(was, position);
    }

}
