namespace WinBoard.Core;

/// <summary>A dictionary word with its accent-folded letters and frequency weight.</summary>
public sealed class WordEntry
{
    public required string Word { get; init; }

    /// <summary>Lowercased a–z letters (accents stripped, ligatures expanded).</summary>
    public required char[] Folded { get; init; }

    /// <summary>0..1, higher = more common (used as a light tie-breaker only).</summary>
    public required double Frequency { get; init; }
}

/// <summary>Words bucketed by first folded letter for fast filtering.</summary>
public sealed class WordList
{
    public required IReadOnlyDictionary<char, List<WordEntry>> ByFirstLetter { get; init; }

    public IEnumerable<WordEntry> All => ByFirstLetter.Values.SelectMany(b => b);

    /// <summary>
    /// Builds a list from frequency-ordered words (first = most common).
    /// Duplicate spellings that fold identically keep the earlier (more frequent) entry.
    /// </summary>
    public static WordList FromOrderedWords(IReadOnlyList<string> words)
    {
        var buckets = new Dictionary<char, List<WordEntry>>();
        var seen = new HashSet<string>();
        int usable = 0;
        int total = Math.Max(1, words.Count);

        foreach (string raw in words)
        {
            string trimmed = raw.Trim();
            if (trimmed.Length == 0)
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
                Frequency = 1.0 - ((double)usable / total),
            };
            usable++;

            if (!buckets.TryGetValue(folded[0], out List<WordEntry>? bucket))
            {
                bucket = [];
                buckets[folded[0]] = bucket;
            }

            bucket.Add(entry);
        }

        return new WordList { ByFirstLetter = buckets };
    }

    public static WordList FromLines(IEnumerable<string> lines) =>
        FromOrderedWords(lines.ToList());
}
