using Mullion.Core.Geometry;

namespace Mullion.Core.Abstractions;

public enum MoveOutcome
{
    /// <summary>Landed within tolerance of the target.</summary>
    Moved,

    /// <summary>Best effort: the window resisted, e.g. a hard minimum size.</summary>
    MovedApproximate,

    /// <summary>Centred at its current size because it cannot be resized.</summary>
    Centred,

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
    public bool Success => Outcome is MoveOutcome.Moved or MoveOutcome.MovedApproximate or MoveOutcome.Centred;
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
    MoveResult MoveForegroundTo(PxRect target);

    /// <summary>Restore the most recent move. Returns false when nothing is on the stack.</summary>
    bool UndoLastMove();

    int UndoDepth { get; }
}
