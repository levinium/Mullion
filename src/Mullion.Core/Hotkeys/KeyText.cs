using System.Globalization;

namespace Mullion.Core.Hotkeys;

/// <summary>
/// How a key is written into the config file and read back.
/// <para>
/// The scan code in hex, with an "e" in front for the E0-prefixed keys: "1e" is
/// A, "e4b" is Left arrow, and "4b" on its own is Num4 - which is the pair the
/// prefix exists to separate, since they are the same code.
/// </para>
/// <para>
/// Not the key's NAME. A name is what a keyboard layout says the key produces,
/// and it changes with the input language; the binding is to the physical key,
/// so that is what gets stored. The name is derived for display only.
/// </para>
/// </summary>
public static class KeyText
{
    /// <summary>The stored form, or null for "no key of its own".</summary>
    public static string? Write(KeyStroke? key) =>
        key is not { } k || k.IsNone
            ? null
            : k.Extended
                ? $"e{k.ScanCode.ToString("x", CultureInfo.InvariantCulture)}"
                : k.ScanCode.ToString("x", CultureInfo.InvariantCulture);

    /// <summary>
    /// Read one back. Anything unreadable answers null - "use the default" -
    /// rather than throwing or guessing, so one bad entry cannot cost a whole
    /// profile.
    /// </summary>
    public static KeyStroke? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();
        var extended = trimmed.StartsWith('e') || trimmed.StartsWith('E');
        var digits = extended ? trimmed[1..] : trimmed;

        if (digits.Length == 0) return null;

        return ushort.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scan)
               && scan != 0
            ? new KeyStroke(scan, extended)
            : null;
    }
}
