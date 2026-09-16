namespace WinBoard.Layouts;

/// <summary>
/// A keyboard page: an optional number row plus the main rows.
/// </summary>
/// <param name="Id">Stable identifier used for persistence.</param>
/// <param name="DisplayName">Human label (e.g. "FR AZERTY").</param>
/// <param name="ShortName">Compact label shown on the space bar (e.g. "FR").</param>
/// <param name="IsAlphabetic">True for letter layouts (AZERTY/QWERTY), false for the symbols page.</param>
/// <param name="NumberRow">The 1–0 row, rendered only when the number-row setting is on.</param>
/// <param name="Rows">The main key rows, top to bottom.</param>
public sealed record KeyboardLayout(
    string Id,
    string DisplayName,
    string ShortName,
    bool IsAlphabetic,
    IReadOnlyList<KeyDefinition> NumberRow,
    IReadOnlyList<IReadOnlyList<KeyDefinition>> Rows);
