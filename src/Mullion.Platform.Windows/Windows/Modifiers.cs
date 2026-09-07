using System.Runtime.Versioning;
using Mullion.Core.Model;
using Mullion.Platform.Windows.Native;

namespace Mullion.Platform.Windows.Windows;

/// <summary>
/// Live modifier state, read straight from the OS.
/// <para>
/// Drag-to-snap cannot use the hotkey engine's tracked modifier state: that is
/// built from the keyboard hook's own event stream, and during a drag the
/// modifier may have gone down before Mullion was ever involved. Asking the OS
/// is the only reading that is true at the moment of the drop.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class Modifiers
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    public static bool IsShiftDown => IsDown(VK_SHIFT);

    public static bool IsControlDown => IsDown(VK_CONTROL);

    public static bool IsAltDown => IsDown(VK_MENU);

    public static bool IsWinDown => IsDown(VK_LWIN) || IsDown(VK_RWIN);

    /// <summary>Whether the named modifier is held right now.</summary>
    public static bool IsDown(DragModifier modifier) => modifier switch
    {
        DragModifier.Shift => IsShiftDown,
        DragModifier.Control => IsControlDown,
        DragModifier.Alt => IsAltDown,
        DragModifier.Win => IsWinDown,
        _ => true,
    };

    // The high bit is "down now"; the low bit is "pressed since last asked" and
    // is deliberately ignored - a drop cares about the state at the drop.
    private static bool IsDown(int key) => (Hooks.GetAsyncKeyState(key) & 0x8000) != 0;
}

