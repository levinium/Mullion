namespace Mullion.Core.Hotkeys;

/// <summary>
/// A hotkey that is not a zone: it does something to the focused window rather
/// than putting it somewhere.
/// <para>
/// These lived in a two-entry array in the engine, bound to the default modifier
/// and unchangeable. That was defensible while there was one of them; it is not
/// once there are several, and it was never defensible that the one hotkey
/// nobody could rebind was the one people would most want to move off Backspace.
/// </para>
/// </summary>
public sealed record GlobalAction(string Command, KeyStroke Key, ChordModifiers? Modifier = null)
{
    /// <summary>Put the window back where it was before the last move.</summary>
    public const string Undo = "undo";

    /// <summary>Minimize the focused window.</summary>
    public const string Minimize = "minimize";

    /// <summary>The chord this answers to, given the configured default.</summary>
    public ChordModifiers ChordWith(ChordModifiers fallback) => Modifier ?? fallback;

    /// <summary>What to call it on screen.</summary>
    public string Title => Describe(Command);

    public static string Describe(string command) => command switch
    {
        Undo => "Undo last move",
        Minimize => "Minimize window",
        _ => command,
    };

    /// <summary>
    /// What Mullion ships with.
    /// <para>
    /// Both sit outside the key surface on purpose. The allocator may claim any
    /// surface key when the desk changes, so an action bound there would stop
    /// working the day a monitor was plugged in - which is also why the settings
    /// screen refuses to put one there.
    /// </para>
    /// </summary>
    public static IReadOnlyList<GlobalAction> Defaults =>
    [
        // Backspace: the undo key everywhere else, and nowhere near the letter
        // block the zones live on.
        new(Undo, KeyStroke.Plain(0x0E)),

        // The tilde key, for the same reason - it is the one key above Tab that
        // no surface will ever want, and it is easy to hit without looking.
        new(Minimize, KeyStroke.Plain(0x29)),
    ];

    /// <summary>
    /// The defaults with any stored changes laid over them.
    /// <para>
    /// Layered rather than replaced, so the stored list only has to carry what
    /// somebody actually changed. An action added in a later version then reaches
    /// people who have already saved settings, instead of only those who never
    /// opened the screen - which is what storing the whole list would have meant.
    /// </para>
    /// </summary>
    public static IReadOnlyList<GlobalAction> Resolve(
        IEnumerable<(string Command, KeyStroke? Key, ChordModifiers? Modifier)>? stored)
    {
        if (stored is null) return Defaults;

        var changes = stored
            .Where(s => s.Key is { } k && !k.IsNone)
            .GroupBy(s => s.Command, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        if (changes.Count == 0) return Defaults;

        return
        [
            .. Defaults.Select(d =>
                changes.TryGetValue(d.Command, out var change)
                    ? d with { Key = change.Key!.Value, Modifier = change.Modifier }
                    : d)
        ];
    }
}
