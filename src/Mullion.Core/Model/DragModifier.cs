namespace Mullion.Core.Model;

/// <summary>
/// Which modifier arms drag-to-snap, or None to have it always armed.
/// <para>
/// A modifier by default rather than always-on: every window drag would
/// otherwise raise zones over the screen, which fights Windows' own edge snap
/// and gets in the way of simply nudging a window. Holding a key says "I mean
/// this one".
/// </para>
/// </summary>
public enum DragModifier
{
    None,
    Shift,
    Control,
    Alt,
    Win,
}