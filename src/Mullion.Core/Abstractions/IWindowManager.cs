using Mullion.Core.Geometry;

namespace Mullion.Core.Abstractions;

public enum MoveOutcome
{
    /// <summary>Landed within tolerance of the target.</summary>
    Moved,

    /// <summary>Best effort: the window resisted, e.g. a hard minimum size.</summary>
    MovedApproximate,

    /// <summary>Centered at its current size because it cannot be resized.</summary>
    Centered,

    SkippedNoForegroundWindow,
    SkippedShellWindow,
    SkippedCloaked,
    SkippedInvalid,

    /// <summary>Target is elevated and we are not.</summary>
    FailedAccessDenied,

    FailedUnknown,
}

public readonly record struct MoveResult(
    MoveOutcome Outcome,
    PxRect Achieved,
    int Attempts,
    string? Note = null)
{
    public bool Success => Outcome is MoveOutcome.Moved or MoveOutcome.MovedApproximate or MoveOutcome.Centered;
}

public sealed record WindowSnapshot(
    nint Handle,
    uint ProcessId,
    string ClassName,
    string Title,
    PxRect ScreenRect,
    bool WasMaximized,
    byte[] PlacementBlob,
    DateTimeOffset TakenUtc);

/// <summary>Moves the focused window. Implemented per platform.</summary>
public interface IWindowManager
{
    /// <summary>
    /// Where a window is on screen, as the user sees it - the visible frame,
    /// not the invisible resize border around it. Null when there is no such
    /// window any more.
    /// </summary>
    PxRect? BoundsOf(nint hwnd);

    /// <summary>
    /// The last size this window had that Mullion did not give it, so filling
    /// a zone can be undone by filling it again. Null for a window Mullion
    /// has never moved.
    /// </summary>
    PxRect? ChosenSizeOf(nint hwnd);

    MoveResult MoveForegroundTo(PxRect target);

    /// <summary>Restore the most recent move. Returns false when nothing is on the stack.</summary>
    bool UndoLastMove();

    /// <summary>
    /// Minimize the focused window. Returns false when there is nothing eligible
    /// to minimize - the desktop, a shell window, or one Mullion may not touch.
    /// </summary>
    bool MinimizeForeground();

    int UndoDepth { get; }
}
