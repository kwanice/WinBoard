namespace WinBoard.Core;

/// <summary>
/// Perennial swipe decoder. SHARK2 is retired.
/// Pipeline: <c>gesture path → spatial letter scores → trie/dict beam → n-gram LM → suggestions</c>.
/// The public <see cref="Decode"/> shape is stable so a neural spatial encoder
/// (e.g. FUTO) can replace <see cref="ISpatialEncoder"/> without rewriting
/// the beam or language model. Local only: no network, no word-pair blacklists.
/// </summary>
public static class SwipeDecoder
{
    internal const int SampleCount = SwipePath.SampleCount;

    internal const double LetterCountWeight = DictionaryBeam.LetterCountWeight;

    internal const double LengthRatioLongWeight = DictionaryBeam.LengthRatioLongWeight;

    internal const double LocationWeight = DictionaryBeam.LocationWeight;

    internal const double HitKeyWeight = DictionaryBeam.HitKeyWeight;

    internal const double HitBoost = DictionaryBeam.HitBoost;

    public static IReadOnlyList<string> Decode(
        IReadOnlyList<char> hitKeys,
        IReadOnlyList<Point2> path,
        IReadOnlyDictionary<char, Point2> centers,
        WordList words,
        double keySize,
        int maxResults = 5,
        string? previousWord = null,
        LanguageModel? language = null,
        ISpatialEncoder? spatial = null)
    {
        EncodedGesture? gesture = (spatial ?? GeometricSpatialEncoder.Shared)
            .Encode(path, centers, keySize, hitKeys);
        if (gesture is null)
        {
            return [];
        }

        List<(WordEntry Entry, double Score)> scored = DictionaryBeam.Search(gesture, words, centers);
        if (scored.Count == 0)
        {
            return [];
        }

        if (language is not null && scored.Count > 1)
        {
            scored = ApplyLanguage(scored, language, previousWord);
        }

        var seenFolded = new HashSet<string>();
        var results = new List<string>();
        foreach ((WordEntry entry, double _) in scored.OrderBy(s => s.Score))
        {
            if (seenFolded.Add(new string(entry.Folded)))
            {
                results.Add(entry.Word);
                if (results.Count >= maxResults)
                {
                    break;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Rescore the spatial beam with P(w|prev). Length already pruned the
    /// long-word tail, so language cannot revive it.
    /// </summary>
    internal static List<(WordEntry Entry, double Score)> ApplyLanguage(
        List<(WordEntry Entry, double Score)> spatial,
        LanguageModel language,
        string? previousWord)
    {
        int pool = Math.Min(spatial.Count, Math.Max(LanguageModel.SpatialPool, 8));
        var ordered = spatial.OrderBy(s => s.Score).ToList();
        var head = ordered.Take(pool).ToList();
        var tail = ordered.Skip(pool).ToList();
        var rescored = new List<(WordEntry Entry, double Score)>(head.Count);
        foreach ((WordEntry entry, double score) in head)
        {
            rescored.Add((entry, score + language.ScoreDelta(entry.Word, previousWord)));
        }

        rescored.AddRange(tail);
        return rescored;
    }

    internal static double ResolvePitch(IReadOnlyDictionary<char, Point2> centers, double keySizeHint) =>
        SwipePath.ResolvePitch(centers, keySizeHint);

    internal static double PolylineLength(IReadOnlyList<Point2> points) => SwipePath.Length(points);

    internal static Point2[] Resample(IReadOnlyList<Point2> points, int count) =>
        SwipePath.Resample(points, count);

    internal static bool LetterCountRejects(int wordLetters, int hitCount) =>
        DictionaryBeam.LetterCountRejects(wordLetters, hitCount);

    internal static double LetterCountPenalty(int wordLetters, int hitCount) =>
        DictionaryBeam.LetterCountPenalty(wordLetters, hitCount);

    internal static bool LengthRatioRejects(
        double templateLength, double userLength, int wordLetters, double pitch) =>
        DictionaryBeam.LengthRatioRejects(templateLength, userLength, wordLetters, pitch);

    internal static double LengthRatioPenalty(double templateLength, double userLength, double pitch) =>
        DictionaryBeam.LengthRatioPenalty(templateLength, userLength, pitch);

    internal static double MeanPairwise(Point2[] a, Point2[] b) =>
        SwipePath.MeanPairwise(a, b);

    /// <summary>
    /// Snap each path sample to the nearest key only when it sits inside the
    /// key (half-pitch). Flyovers between keys are gaps, not extra letters.
    /// </summary>
    public static IReadOnlyList<char> BuildObservedKeys(
        IReadOnlyList<Point2> samples,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        double pitch = SwipePath.ResolvePitch(centers, keySize);
        var observed = new List<char>();
        const double snapRadius = 0.48;
        double maxDist = snapRadius * pitch;
        foreach (Point2 sample in samples)
        {
            char best = '\0';
            double bestDist = double.MaxValue;
            foreach ((char letter, Point2 center) in centers)
            {
                double d = sample.DistanceTo(center);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = letter;
                }
            }

            if (best == '\0' || bestDist > maxDist)
            {
                continue;
            }

            if (observed.Count == 0 || observed[^1] != best)
            {
                observed.Add(best);
            }
        }

        return observed;
    }
}
