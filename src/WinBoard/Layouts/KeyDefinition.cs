namespace WinBoard.Layouts;

public enum KeyKind
{
    Character,
    Backspace,
    Space,
}

/// <param name="Label">Text drawn on the key (usually uppercase, like a physical keycap).</param>
/// <param name="Kind">Injection behavior.</param>
/// <param name="Character">Unicode character to inject when <see cref="Kind"/> is Character or Space.</param>
/// <param name="WidthUnits">Relative width (1 = letter key).</param>
public sealed record KeyDefinition(
    string Label,
    KeyKind Kind,
    char? Character = null,
    double WidthUnits = 1.0);
