using System.Reflection;
using System.Globalization;

namespace WinBoard.Core;

/// <summary>A dictionary word with its accent-folded letters and frequency weight.</summary>
public sealed class WordEntry
{
    public required string Word { get; init; }

    /// <summary>Lowercased a–z letters (accents stripped, ligatures expanded).</summary>
    public required char[] Folded { get; init; }

    /// <summary>Same letters as <see cref="Folded"/>, cached for HashSet keys.</summary>
    public required string FoldKey { get; init; }

    /// <summary>0..1, higher = more common (used as a light tie-breaker only).</summary>
    public required double Frequency { get; init; }
}

/// <summary>Words bucketed by first folded letter for fast filtering.</summary>
public sealed class WordList
{
    public required IReadOnlyDictionary<char, List<WordEntry>> ByFirstLetter { get; init; }

    /// <summary>
    /// Words grouped by first then last folded letter. Swipe start/end
    /// buckets use this instead of scanning an entire first-letter list.
    /// </summary>
    public required IReadOnlyDictionary<char, Dictionary<char, List<WordEntry>>> ByFirstAndLast { get; init; }

    /// <summary>Number of unique folded entries kept after loading.</summary>
    public required int Count { get; init; }

    private WordTrie? _trie;
    private readonly object _trieGate = new();

    /// <summary>Prefix trie over <see cref="All"/>, built once per list.</summary>
    public WordTrie Trie
    {
        get
        {
            if (_trie is not null)
            {
                return _trie;
            }

            lock (_trieGate)
            {
                return _trie ??= WordTrie.Build(this);
            }
        }
    }

    /// <summary>Force the trie so the first swipe does not pay construction.</summary>
    public WordTrie EnsureReady() => Trie;

    public IEnumerable<WordEntry> All => ByFirstLetter.Values.SelectMany(b => b);

    /// <summary>
    /// Builds a list from frequency-ordered words (first = most common).
    /// Duplicate spellings that fold identically keep the earlier (more frequent) entry.
    /// </summary>
    public static WordList FromOrderedWords(IReadOnlyList<string> words)
    {
        var buckets = new Dictionary<char, List<WordEntry>>();
        var firstLast = new Dictionary<char, Dictionary<char, List<WordEntry>>>();
        var seen = new HashSet<string>();
        int usable = 0;
        int total = Math.Max(1, words.Count);

        foreach (string raw in words)
        {
            string trimmed = raw.Trim();
            if (!IsPlainSwipeWord(trimmed))
            {
                continue;
            }

            char[] folded = TextFolding.ToLetters(trimmed);
            if (folded.Length < 2)
            {
                continue;
            }

            string foldKey = new(folded);
            if (!seen.Add(foldKey))
            {
                continue;
            }

            var entry = new WordEntry
            {
                Word = trimmed,
                Folded = folded,
                FoldKey = foldKey,
                Frequency = 1.0 - ((double)usable / total),
            };
            usable++;

            if (!buckets.TryGetValue(folded[0], out List<WordEntry>? bucket))
            {
                bucket = [];
                buckets[folded[0]] = bucket;
            }

            bucket.Add(entry);

            if (!firstLast.TryGetValue(folded[0], out Dictionary<char, List<WordEntry>>? byLast))
            {
                byLast = [];
                firstLast[folded[0]] = byLast;
            }

            char last = folded[^1];
            if (!byLast.TryGetValue(last, out List<WordEntry>? tail))
            {
                tail = [];
                byLast[last] = tail;
            }

            tail.Add(entry);
        }

        return new WordList { ByFirstLetter = buckets, ByFirstAndLast = firstLast, Count = usable };
    }

    /// <summary>
    /// Parses a lexicon file: one word per line, optional trailing frequency weight.
    /// Empty lines and <c>#</c> comments are ignored.
    /// </summary>
    public static WordList FromLines(IEnumerable<string> lines)
    {
        var words = new List<string>();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                words.Add(parts[0]);
            }
            else if (parts.Length == 2
                     && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                // Numeric frequency suffix is metadata, not part of the word.
                words.Add(parts[0]);
            }
        }

        return FromOrderedWords(words);
    }

    /// <summary>
    /// Loads the shipped FR or EN lexicon embedded in WinBoard.Core (offline, no network).
    /// </summary>
    public static WordList LoadLanguage(string language) => FromLines(ReadLexiconLines(language));

    /// <summary>
    /// FR ∪ EN plus keyboard-layout names missing from both frequency lists
    /// (<c>azerty</c>). Swipe on AZERTY must still find EN targets (hello, swipe,
    /// qwerty, thanks, windows) without a per-word blacklist.
    /// </summary>
    public static WordList LoadBilingual()
    {
        var lines = new List<string>(210_000)
        {
            "azerty",
            "qwerty",
        };
        lines.AddRange(ReadLexiconLines("fr"));
        lines.AddRange(ReadLexiconLines("en"));
        return FromLines(lines);
    }

    internal static IEnumerable<string> ReadLexiconLines(string language)
    {
        string resource = $"WinBoard.Core.Dictionaries.words_{language}.txt";
        Assembly assembly = typeof(WordList).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            throw new FileNotFoundException($"Embedded dictionary not found: {resource}");
        }

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    /// <summary>True if a spelling (accent-insensitive) is in the list.</summary>
    public bool Contains(string word)
    {
        if (!IsPlainSwipeWord(word))
        {
            return false;
        }

        char[] folded = TextFolding.ToLetters(word);
        if (folded.Length < 2 || !ByFirstLetter.TryGetValue(folded[0], out List<WordEntry>? bucket))
        {
            return false;
        }

        foreach (WordEntry entry in bucket)
        {
            if (entry.Folded.AsSpan().SequenceEqual(folded))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Swipe entries are a single Unicode-letter token. Punctuation and
    /// whitespace would disappear during folding and turn compounds into a
    /// misleading longer gesture candidate.
    /// </summary>
    private static bool IsPlainSwipeWord(string word) =>
        word.Length >= 2 && word.All(char.IsLetter);
}
