namespace WinBoard.Layouts;

public sealed record KeyboardLayout(
    string Id,
    string DisplayName,
    IReadOnlyList<IReadOnlyList<KeyDefinition>> Rows);
