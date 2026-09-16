namespace WinBoard.Layouts;

/// <summary>
/// Built-in keyboard pages: FR AZERTY, EN QWERTY, and a shared symbols page.
/// Secondary glyphs and long-press specials mirror a Gboard-style phone keyboard.
/// </summary>
public static class LayoutCatalog
{
    public const string AzertyId = "fr-azerty";
    public const string QwertyId = "en-qwerty";
    public const string SymbolsId = "symbols";

    public static KeyboardLayout Azerty { get; } = CreateAzerty();

    public static KeyboardLayout Qwerty { get; } = CreateQwerty();

    public static KeyboardLayout Symbols { get; } = CreateSymbols();

    /// <summary>Alphabetic layouts offered in Settings.</summary>
    public static IReadOnlyList<KeyboardLayout> Alphabetic { get; } = [Azerty, Qwerty];

    public static KeyboardLayout GetById(string? id) => id switch
    {
        QwertyId => Qwerty,
        SymbolsId => Symbols,
        _ => Azerty,
    };

    // --- Number rows -------------------------------------------------------

    private static IReadOnlyList<KeyDefinition> NumberRow() =>
    [
        Digit('1', "¹", "¹ ½ ⅓ ¼"),
        Digit('2', "²", "² ⅔"),
        Digit('3', "³", "³ ¾"),
        Digit('4', "⁴"),
        Digit('5', "⁵", "⅝"),
        Digit('6', "⁶"),
        Digit('7', "⁷"),
        Digit('8', "⁸"),
        Digit('9', "⁹"),
        Digit('0', "⁰", "ⁿ ∅"),
    ];

    // --- AZERTY ------------------------------------------------------------

    private static KeyboardLayout CreateAzerty()
    {
        return new KeyboardLayout(
            AzertyId,
            "FR AZERTY",
            "FR",
            IsAlphabetic: true,
            NumberRow(),
            [
                [
                    Letter('a', "%", "à â æ á ä ã å ā %"),
                    Letter('z', "\\", "\\"),
                    Letter('e', "|", "é è ê ë ę ē ė |"),
                    Letter('r', "=", "="),
                    Letter('t', "[", "["),
                    Letter('y', "]", "ÿ ]"),
                    Letter('u', "<", "ù û ü ú ū <"),
                    Letter('i', ">", "î ï ì í į ī >"),
                    Letter('o', "{", "ô œ ö ò ó õ ø ō {"),
                    Letter('p', "}", "}"),
                ],
                [
                    Letter('q', "@", "@"),
                    Letter('s', "#", "#"),
                    Letter('d', "€", "€ $ £ ¢ ¥"),
                    Letter('f', "_", "_"),
                    Letter('g', "&", "&"),
                    Letter('h', "-", "-"),
                    Letter('j', "+", "+"),
                    Letter('k', "(", "("),
                    Letter('l', ")", ")"),
                    Letter('m', "/", "/"),
                ],
                [
                    ShiftKey(),
                    Letter('w', "*", "* ω"),
                    Letter('x', "\"", "\""),
                    Letter('c', "'", "ç ć č '"),
                    Letter('v', ":", ":"),
                    Letter('b', ";", ";"),
                    Letter('n', "!", "ñ ń !"),
                    Punct('\'', label: "'", secondary: "?", longPress: "? ¿ ' `"),
                    BackspaceKey(),
                ],
                BottomRow(),
            ]);
    }

    // --- QWERTY ------------------------------------------------------------

    private static KeyboardLayout CreateQwerty()
    {
        return new KeyboardLayout(
            QwertyId,
            "EN QWERTY",
            "EN",
            IsAlphabetic: true,
            NumberRow(),
            [
                [
                    Letter('q', "1", "1"),
                    Letter('w', "2", "2"),
                    Letter('e', "3", "é è ê ë ē 3"),
                    Letter('r', "4", "4"),
                    Letter('t', "5", "5"),
                    Letter('y', "6", "ÿ 6"),
                    Letter('u', "7", "ù û ü ú 7"),
                    Letter('i', "8", "î ï ì í 8"),
                    Letter('o', "9", "ô ö ò ó õ ø 9"),
                    Letter('p', "0", "0"),
                ],
                [
                    Letter('a', "@", "à á â ä ã å ā @"),
                    Letter('s', "#", "ß ś š #"),
                    Letter('d', "$", "$"),
                    Letter('f', "%", "%"),
                    Letter('g', "&", "&"),
                    Letter('h', "-", "-"),
                    Letter('j', "+", "+"),
                    Letter('k', "(", "("),
                    Letter('l', ")", ")"),
                ],
                [
                    ShiftKey(),
                    Letter('z', "*", "* ź ž ż"),
                    Letter('x', "\"", "\""),
                    Letter('c', "'", "ç ć č '"),
                    Letter('v', ":", ":"),
                    Letter('b', ";", ";"),
                    Letter('n', "!", "ñ ń !"),
                    Letter('m', "?", "?"),
                    BackspaceKey(),
                ],
                BottomRow(),
            ]);
    }

    // --- Symbols page ------------------------------------------------------

    private static KeyboardLayout CreateSymbols()
    {
        return new KeyboardLayout(
            SymbolsId,
            "Symboles",
            "?123",
            IsAlphabetic: false,
            [],
            [
                Row("1234567890"),
                [
                    Sym('@'), Sym('#'), Sym('€', "$ £ ¢ ¥ ₩"), Sym('_'), Sym('&'),
                    Sym('-'), Sym('+'), Sym('('), Sym(')'), Sym('/'),
                ],
                [
                    Punct('*', label: "*"),
                    Punct('"', label: "\""),
                    Punct('\'', label: "'"),
                    Punct(':', label: ":"),
                    Punct(';', label: ";"),
                    Punct('!', label: "!"),
                    Punct('?', label: "?"),
                    BackspaceKey(),
                ],
                BottomRow(),
            ]);
    }

    // --- Shared bottom row -------------------------------------------------

    private static IReadOnlyList<KeyDefinition> BottomRow() =>
    [
        new KeyDefinition(KeyKind.Symbols, Label: "?123", WidthUnits: 1.5),
        Punct(',', label: ","),
        new KeyDefinition(KeyKind.Emoji, Label: "🙂"),
        new KeyDefinition(KeyKind.Space, Character: ' ', WidthUnits: 4.5),
        Punct('.', label: ".", secondary: null, longPress: ". … ·"),
        new KeyDefinition(KeyKind.Enter, Label: "⏎", WidthUnits: 1.5),
    ];

    // --- Key factories -----------------------------------------------------

    private static KeyDefinition ShiftKey() =>
        new(KeyKind.Shift, Label: "⇧", WidthUnits: 1.5);

    private static KeyDefinition BackspaceKey() =>
        new(KeyKind.Backspace, Label: "⌫", WidthUnits: 1.5, Repeatable: true);

    private static KeyDefinition Letter(char c, string? secondary = null, string? longPress = null) =>
        new(
            KeyKind.Character,
            Character: c,
            SecondaryGlyph: secondary,
            LongPress: Split(longPress));

    private static KeyDefinition Digit(char c, string? secondary = null, string? longPress = null) =>
        new(
            KeyKind.Character,
            Character: c,
            SecondaryGlyph: secondary,
            LongPress: Split(longPress),
            Repeatable: true);

    private static KeyDefinition Sym(char c, string? longPress = null) =>
        new(
            KeyKind.Character,
            Character: c,
            LongPress: Split(longPress),
            Repeatable: true);

    private static KeyDefinition Punct(char c, string label, string? secondary = null, string? longPress = null) =>
        new(
            KeyKind.Character,
            Character: c,
            Label: label,
            SecondaryGlyph: secondary,
            LongPress: Split(longPress));

    private static IReadOnlyList<KeyDefinition> Row(string chars)
    {
        var keys = new KeyDefinition[chars.Length];
        for (int i = 0; i < chars.Length; i++)
        {
            keys[i] = new KeyDefinition(KeyKind.Character, Character: chars[i], Repeatable: true);
        }

        return keys;
    }

    private static IReadOnlyList<string>? Split(string? longPress)
    {
        if (string.IsNullOrWhiteSpace(longPress))
        {
            return null;
        }

        return longPress.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
