namespace Mullion.Core.Hotkeys;

/// <summary>
/// One physical key, as a binding identifies it.
/// <para>
/// A scan code alone is not enough, and the reason is not obscure: the arrow
/// keys and the numpad share theirs. Left arrow and Num4 are both 0x4B, Up and
/// Num8 are both 0x48, and Windows tells them apart with a single "extended"
/// bit on the event. Binding by scan code alone, Win+Left would also fire on
/// Num4 - and on the numpad key surface that is a zone key, so a user would be
/// colliding with their own layout.
/// </para>
/// <para>
/// It stayed hidden while bindings could only land on the letter block, where
/// nothing is extended. Opening the surface up to any key on the keyboard is
/// what makes it reachable, so the bit comes along with it.
/// </para>
/// </summary>
/// <param name="ScanCode">Set 1 scan code, as the low-level hook reports it.</param>
/// <param name="Extended">
/// The E0 prefix: the navigation cluster, the arrows, right Ctrl and Alt, and
/// the numpad's Enter and divide.
/// </param>
public readonly record struct KeyStroke(ushort ScanCode, bool Extended)
{
    /// <summary>A key on the main block, where nothing carries the E0 prefix.</summary>
    public static KeyStroke Plain(ushort scanCode) => new(scanCode, false);

    /// <summary>Nothing at all, for "no key chosen".</summary>
    public static readonly KeyStroke None = new(0, false);

    public bool IsNone => ScanCode == 0;

    public override string ToString() => Extended ? $"e{ScanCode:x2}" : $"{ScanCode:x2}";
}
