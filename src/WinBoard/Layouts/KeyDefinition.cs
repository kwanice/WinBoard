namespace WinBoard.Layouts;

/// <summary>
/// Behavior of a key when tapped. Comma/period are plain <see cref="Character"/> keys.
/// </summary>
public enum KeyKind
{
    Character,
    Shift,
    Backspace,
    Enter,
    Space,
    Symbols,
    Emoji,
}

/// <summary>
/// A single key on the on-screen keyboard.
/// </summary>
/// <param name="Kind">Injection / action behavior.</param>
/// <param name="Character">
/// Primary character to inject (lowercase for letters; Shift uppercases at inject time).
/// </param>
/// <param name="Label">Explicit cap label; when null it is derived from <see cref="Character"/>.</param>
/// <param name="SecondaryGlyph">Small glyph drawn in the corner of the cap (Gboard style).</param>
/// <param name="LongPress">Characters offered in the long-press popup (accents, symbols…).</param>
/// <param name="WidthUnits">Relative width (1 = a letter key).</param>
/// <param name="Repeatable">True if holding the key repeats it (e.g. Backspace).</param>
public sealed record KeyDefinition(
    KeyKind Kind = KeyKind.Character,
    char? Character = null,
    string? Label = null,
    string? SecondaryGlyph = null,
    IReadOnlyList<string>? LongPress = null,
    double WidthUnits = 1.0,
    bool Repeatable = false)
{
    /// <summary>True for keys that use a slightly darker "function" background.</summary>
    public bool IsFunctionKey =>
        Kind is KeyKind.Shift or KeyKind.Backspace or KeyKind.Enter or KeyKind.Symbols or KeyKind.Emoji;

    /// <summary>Label shown when Shift/Caps is not engaged.</summary>
    public string DisplayLabel => Label ?? (Character?.ToString() ?? string.Empty);

    public bool HasLongPress => LongPress is { Count: > 0 };
}
