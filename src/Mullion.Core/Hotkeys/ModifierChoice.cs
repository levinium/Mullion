namespace Mullion.Core.Hotkeys;

/// <summary>
/// The modifier every zone hotkey is taken with, as a name config can store and
/// the UI can show.
/// <para>
/// Win is the default and the reason the app exists: it is the combination
/// RegisterHotKey cannot claim, which is why Mullion installs a hook at all. The
/// alternatives are for anyone who would rather leave the Windows key alone, or
/// who already has Win+letter bound elsewhere.
/// </para>
/// </summary>
public static class ModifierChoice
{
    /// <summary>Offered in the UI, in the order they are offered.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "Win",
        "Ctrl+Alt",
        "Ctrl+Shift",
        "Alt+Shift",
        "Win+Shift",
        "Alt",
    ];

    public const string Default = "Win";

    public static ChordModifiers Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ChordModifiers.Win;

        var mods = ChordModifiers.None;

        foreach (var part in name.Split(['+', ' ', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            mods |= part.Trim().ToLowerInvariant() switch
            {
                "win" or "windows" or "meta" => ChordModifiers.Win,
                "ctrl" or "control" => ChordModifiers.Control,
                "alt" or "menu" => ChordModifiers.Alt,
                "shift" => ChordModifiers.Shift,
                _ => ChordModifiers.None,
            };
        }

        // An unrecognised name must not silently unbind every hotkey: a chord
        // with no modifier would fire on a bare letter, which is far worse than
        // ignoring the setting.
        return mods == ChordModifiers.None ? ChordModifiers.Win : mods;
    }

    /// <summary>How the chord reads in the UI, e.g. "Win" or "Ctrl+Alt".</summary>
    public static string Format(ChordModifiers modifiers)
    {
        if (modifiers == ChordModifiers.None) return string.Empty;

        var parts = new List<string>(4);

        // Ordered the way keyboards are labelled, not the way the flags happen
        // to be numbered.
        if (modifiers.HasFlag(ChordModifiers.Win)) parts.Add("Win");
        if (modifiers.HasFlag(ChordModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ChordModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ChordModifiers.Shift)) parts.Add("Shift");

        return string.Join("+", parts);
    }

    /// <summary>
    /// Whether this choice needs the Start-menu suppression machinery. Only the
    /// Windows key opens something on release, so every other combination can
    /// skip it.
    /// </summary>
    public static bool NeedsWinKeySuppression(ChordModifiers modifiers) =>
        modifiers.HasFlag(ChordModifiers.Win);
}
