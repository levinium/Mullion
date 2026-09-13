using System.Collections.Frozen;

namespace Mullion.Core.Hotkeys;

/// <summary>
/// Readable names for physical keys.
/// <para>
/// A key surface carries labels for the fifteen keys it covers, which was enough
/// while a binding could not land anywhere else. Once any key will do, every key
/// needs a name - including the ones no surface mentions.
/// </para>
/// <para>
/// A table rather than MapVirtualKey or GetKeyNameText, for the same reason the
/// surfaces carry fallback labels: this is Core, which has no Win32, and the
/// names are what gets drawn on a diagram and written into a config file. A
/// stable "Q" that is occasionally the wrong letter on an exotic layout is a
/// better thing to store than a name that changes with the input language.
/// </para>
/// </summary>
public static class KeyNames
{
    /// <summary>What to show for a key nothing here knows about.</summary>
    public static string Unknown(KeyStroke key) =>
        key.IsNone ? "—" : $"Key {key.ScanCode:X2}{(key.Extended ? "e" : string.Empty)}";

    public static string Of(KeyStroke key) =>
        key.Extended && Extended.TryGetValue(key.ScanCode, out var e) ? e
        : !key.Extended && Plain.TryGetValue(key.ScanCode, out var p) ? p
        : Unknown(key);

    /// <summary>Whether this key has a name, i.e. whether it is one we can offer.</summary>
    public static bool IsKnown(KeyStroke key) =>
        key.Extended ? Extended.ContainsKey(key.ScanCode) : Plain.ContainsKey(key.ScanCode);

    /// <summary>
    /// Keys that must never be bound, whatever the user presses.
    /// <para>
    /// Not a matter of taste. Escape cancels a capture, so it can never be the
    /// thing captured; and a modifier is half of a chord rather than the key at
    /// the end of one, so binding it would mean a hotkey that fires as you reach
    /// for another.
    /// </para>
    /// </summary>
    public static bool IsBindable(KeyStroke key) =>
        !key.IsNone && !Reserved.Contains(key.ScanCode) && IsKnown(key);

    private static readonly FrozenSet<ushort> Reserved = new ushort[]
    {
        0x01,        // Escape - cancels the capture
        0x1D,        // Ctrl
        0x2A, 0x36,  // Shift
        0x38,        // Alt
        0x5B, 0x5C,  // Win, though these arrive as extended
        0x3A,        // Caps Lock, which Windows treats specially
    }.ToFrozenSet();

    /// <summary>Set 1 scan codes with no E0 prefix: the main block and the numpad.</summary>
    private static readonly FrozenDictionary<ushort, string> Plain =
        new Dictionary<ushort, string>
        {
            [0x02] = "1", [0x03] = "2", [0x04] = "3", [0x05] = "4", [0x06] = "5",
            [0x07] = "6", [0x08] = "7", [0x09] = "8", [0x0A] = "9", [0x0B] = "0",
            [0x0C] = "-", [0x0D] = "=", [0x0E] = "Backspace", [0x0F] = "Tab",

            [0x10] = "Q", [0x11] = "W", [0x12] = "E", [0x13] = "R", [0x14] = "T",
            [0x15] = "Y", [0x16] = "U", [0x17] = "I", [0x18] = "O", [0x19] = "P",
            [0x1A] = "[", [0x1B] = "]", [0x1C] = "Enter",

            [0x1E] = "A", [0x1F] = "S", [0x20] = "D", [0x21] = "F", [0x22] = "G",
            [0x23] = "H", [0x24] = "J", [0x25] = "K", [0x26] = "L",
            [0x27] = ";", [0x28] = "'", [0x29] = "`", [0x2B] = "\\",

            [0x2C] = "Z", [0x2D] = "X", [0x2E] = "C", [0x2F] = "V", [0x30] = "B",
            [0x31] = "N", [0x32] = "M", [0x33] = ",", [0x34] = ".", [0x35] = "/",

            [0x37] = "Num*", [0x39] = "Space",

            [0x3B] = "F1", [0x3C] = "F2", [0x3D] = "F3", [0x3E] = "F4",
            [0x3F] = "F5", [0x40] = "F6", [0x41] = "F7", [0x42] = "F8",
            [0x43] = "F9", [0x44] = "F10", [0x57] = "F11", [0x58] = "F12",

            [0x45] = "NumLock", [0x46] = "ScrollLock",

            [0x47] = "Num7", [0x48] = "Num8", [0x49] = "Num9", [0x4A] = "Num-",
            [0x4B] = "Num4", [0x4C] = "Num5", [0x4D] = "Num6", [0x4E] = "Num+",
            [0x4F] = "Num1", [0x50] = "Num2", [0x51] = "Num3",
            [0x52] = "Num0", [0x53] = "Num.",
        }.ToFrozenDictionary();

    /// <summary>
    /// The E0-prefixed keys. These share scan codes with the numpad, which is the
    /// whole reason a binding records the prefix as well as the code: Left arrow
    /// and Num4 are both 0x4B.
    /// </summary>
    private static readonly FrozenDictionary<ushort, string> Extended =
        new Dictionary<ushort, string>
        {
            [0x1C] = "NumEnter", [0x35] = "Num/",
            [0x47] = "Home", [0x48] = "Up", [0x49] = "PageUp",
            [0x4B] = "Left", [0x4D] = "Right",
            [0x4F] = "End", [0x50] = "Down", [0x51] = "PageDown",
            [0x52] = "Insert", [0x53] = "Delete",
        }.ToFrozenDictionary();
}
