namespace WinBoard.Layouts;

/// <summary>
/// Built-in FR AZERTY and EN QWERTY letter layouts for the MVP skeleton.
/// </summary>
public static class LayoutCatalog
{
    public const string AzertyId = "fr-azerty";
    public const string QwertyId = "en-qwerty";

    public static KeyboardLayout Azerty { get; } = CreateAzerty();

    public static KeyboardLayout Qwerty { get; } = CreateQwerty();

    public static IReadOnlyList<KeyboardLayout> All { get; } = [Azerty, Qwerty];

    public static KeyboardLayout GetById(string id) =>
        All.FirstOrDefault(layout => string.Equals(layout.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Azerty;

    private static KeyboardLayout CreateAzerty()
    {
        return new KeyboardLayout(
            AzertyId,
            "FR AZERTY",
            [
                LetterRow("azertyuiop"),
                LetterRow("qsdfghjklm"),
                [
                    ..LetterKeys("wxcvbn"),
                    new KeyDefinition("⌫", KeyKind.Backspace, WidthUnits: 1.6),
                ],
                [
                    new KeyDefinition("espace", KeyKind.Space, ' ', WidthUnits: 6),
                ],
            ]);
    }

    private static KeyboardLayout CreateQwerty()
    {
        return new KeyboardLayout(
            QwertyId,
            "EN QWERTY",
            [
                LetterRow("qwertyuiop"),
                LetterRow("asdfghjkl"),
                [
                    ..LetterKeys("zxcvbnm"),
                    new KeyDefinition("⌫", KeyKind.Backspace, WidthUnits: 1.6),
                ],
                [
                    new KeyDefinition("space", KeyKind.Space, ' ', WidthUnits: 6),
                ],
            ]);
    }

    private static IReadOnlyList<KeyDefinition> LetterRow(string letters) => LetterKeys(letters);

    private static KeyDefinition[] LetterKeys(string letters)
    {
        var keys = new KeyDefinition[letters.Length];
        for (int i = 0; i < letters.Length; i++)
        {
            char lower = letters[i];
            keys[i] = new KeyDefinition(char.ToUpperInvariant(lower).ToString(), KeyKind.Character, lower);
        }

        return keys;
    }
}
