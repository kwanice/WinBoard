using System.Globalization;
using System.Reflection;

namespace WinBoard.Core;

/// <summary>
/// Compact offline unigram + bigram prior (FR+EN). No network, no word-pair
/// blacklists. Used only to rescore a short spatial candidate pool.
/// <see cref="LanguageWeight"/> is smaller than a length-gate miss so a common
/// phrase cannot revive a 12-letter word on a ~7-key gesture.
/// </summary>
public sealed class LanguageModel
{
    /// <summary>
    /// Added to the spatial score (lower is better). ~0.38 can flip a close
    /// neighbor (~0.2–0.4 spatial gap) but not a clear geometry lead
    /// (<see cref="LanguageLockGap"/>) or a length/hit-key cliff (~2+).
    /// </summary>
    public const double LanguageWeight = 0.38;

    /// <summary>
    /// Spatial gap above which P(w|prev) is not applied. Frequency cannot
    /// overtake a clearly better path.
    /// </summary>
    public const double LanguageLockGap = 0.48;

    /// <summary>How many spatial survivors are rescored with P(w|prev).</summary>
    public const int SpatialPool = 20;

    private readonly Dictionary<string, double> _unigram;
    private readonly Dictionary<string, Dictionary<string, double>> _bigram;
    private readonly Dictionary<string, double> _prevTotal;

    private LanguageModel(
        Dictionary<string, double> unigram,
        Dictionary<string, Dictionary<string, double>> bigram,
        Dictionary<string, double> prevTotal)
    {
        _unigram = unigram;
        _bigram = bigram;
        _prevTotal = prevTotal;
    }

    /// <summary>Build from explicit tables (tests) or shipped files + a word list.</summary>
    public static LanguageModel FromTables(
        IReadOnlyDictionary<string, double> unigrams,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? bigrams = null)
    {
        var uni = new Dictionary<string, double>();
        foreach ((string word, double freq) in unigrams)
        {
            string key = FoldKey(word);
            if (key.Length >= 2)
            {
                uni[key] = Math.Clamp(freq, 0, 1);
            }
        }

        var bi = new Dictionary<string, Dictionary<string, double>>();
        var totals = new Dictionary<string, double>();
        if (bigrams is not null)
        {
            foreach ((string prev, IReadOnlyDictionary<string, double> next) in bigrams)
            {
                string prevKey = FoldKey(prev);
                if (prevKey.Length < 2)
                {
                    continue;
                }

                var row = new Dictionary<string, double>();
                double total = 0;
                foreach ((string word, double weight) in next)
                {
                    string wordKey = FoldKey(word);
                    if (wordKey.Length < 2 || weight <= 0)
                    {
                        continue;
                    }

                    row[wordKey] = weight;
                    total += weight;
                }

                if (row.Count > 0)
                {
                    bi[prevKey] = row;
                    totals[prevKey] = total;
                }
            }
        }

        return new LanguageModel(uni, bi, totals);
    }

    /// <summary>Unigrams from the lexicon rank; bigrams from the embedded table.</summary>
    public static LanguageModel LoadLanguage(string language, WordList words)
    {
        var unigrams = new Dictionary<string, double>();
        foreach (WordEntry entry in words.All)
        {
            string key = new(entry.Folded);
            if (!unigrams.ContainsKey(key))
            {
                unigrams[key] = entry.Frequency;
            }
        }

        var bigrams = new Dictionary<string, IReadOnlyDictionary<string, double>>();
        foreach ((string prev, string word, double weight) in ReadBigramResource(language))
        {
            if (!bigrams.TryGetValue(prev, out IReadOnlyDictionary<string, double>? row))
            {
                var writable = new Dictionary<string, double>();
                bigrams[prev] = writable;
                row = writable;
            }

            ((Dictionary<string, double>)row)[word] = weight;
        }

        return FromTables(unigrams, bigrams);
    }

    /// <summary>
    /// Cost in 0..1 (0 = common). Interpolates P(w|prev) with the unigram when
    /// a previous word is known; otherwise a light unigram fallback.
    /// </summary>
    public double Cost(string word, string? previousWord)
    {
        string w = FoldKey(word);
        if (w.Length < 2)
        {
            return 1;
        }

        double uni = Unigram(w);
        string prev = FoldKey(previousWord ?? string.Empty);
        if (prev.Length >= 2
            && _bigram.TryGetValue(prev, out Dictionary<string, double>? row)
            && _prevTotal.TryGetValue(prev, out double total)
            && total > 0)
        {
            row.TryGetValue(w, out double count);
            if (count > 0)
            {
                double attested = 0.55 + (0.45 * (count / total));
                return 1.0 - Math.Clamp(Math.Max(uni, attested), 0, 1);
            }

            // Seen left-context, but this word is not an attested continuation.
            // Light penalty — not a ban.
            return 1.0 - Math.Clamp(0.35 * uni, 0, 1);
        }

        return 1.0 - uni;
    }

    /// <summary><see cref="LanguageWeight"/> × cost — added to the spatial score.</summary>
    public double ScoreDelta(string word, string? previousWord) =>
        LanguageWeight * Cost(word, previousWord);

    internal bool HasBigram(string previousWord, string word)
    {
        string prev = FoldKey(previousWord);
        string w = FoldKey(word);
        return prev.Length >= 2
            && _bigram.TryGetValue(prev, out Dictionary<string, double>? row)
            && row.ContainsKey(w);
    }

    private double Unigram(string folded)
    {
        if (_unigram.TryGetValue(folded, out double freq))
        {
            return Math.Clamp(freq, 0.02, 1);
        }

        return 0.02;
    }

    public static string FoldKey(string value)
    {
        char[] letters = TextFolding.ToLetters(value);
        return letters.Length == 0 ? string.Empty : new string(letters);
    }

    internal static IEnumerable<(string Prev, string Word, double Weight)> ReadBigramResource(string language)
    {
        string resource = $"WinBoard.Core.Dictionaries.bigrams_{language}.txt";
        Assembly assembly = typeof(LanguageModel).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            yield break;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string prev = FoldKey(parts[0]);
            string word = FoldKey(parts[1]);
            double weight = 1;
            if (parts.Length >= 3
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                && parsed > 0)
            {
                weight = parsed;
            }

            if (prev.Length >= 2 && word.Length >= 2)
            {
                yield return (prev, word, weight);
            }
        }
    }
}
