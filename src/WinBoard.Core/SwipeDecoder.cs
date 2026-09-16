namespace WinBoard.Core;

/// <summary>
/// Offline shape-writing decoder (SHARK2-inspired, no ML, no network).
///
/// Ranking is driven by the <b>spatial key sequence actually traced</b>, not by
/// first/last letter + a loose polyline:
///   1. Resample the pointer path and snap each sample to the nearest key.
///   2. Score each dictionary word with:
///      - spatial Levenshtein vs that noisy key sequence (M vs N is expensive
///        when those keys are far apart),
///      - DTW of the word's key-center polyline vs the path,
///      - monotonic nearest-point cost so letters must match the path <i>in order</i>,
///      - first/last proximity,
///      - a small frequency tie-break only.
/// Letters whose key sits far from the entire path are hard-penalized.
/// </summary>
public static class SwipeDecoder
{
    private const int SampleCount = 64;
    private const double NearbyKeys = 1.7;

    public static IReadOnlyList<string> Decode(
        IReadOnlyList<char> hitKeys,
        IReadOnlyList<Point2> path,
        IReadOnlyDictionary<char, Point2> centers,
        WordList words,
        double keySize,
        int maxResults = 5)
    {
        if (path.Count < 2 || keySize <= 0 || centers.Count == 0)
        {
            return [];
        }

        Point2[] resampled = Resample(path, SampleCount);
        IReadOnlyList<char> snapped = BuildObservedKeys(resampled, centers, keySize);
        // Keys the pointer actually entered (from the UI) beat a noisy nearest-key
        // trace that picks up every letter the stroke flies over.
        IReadOnlyList<char> sequence = hitKeys.Count >= 2 ? hitKeys : snapped;
        if (sequence.Count < 2)
        {
            return [];
        }

        Point2 start = resampled[0];
        Point2 end = resampled[^1];

        var scored = new List<(WordEntry Entry, double Score)>();
        foreach (WordEntry entry in EnumerateCandidates(words, start, end, sequence, centers, keySize))
        {
            if (!TryWordCenters(entry.Folded, centers, out List<Point2> wordCenters))
            {
                continue;
            }

            double score = ScoreWord(
                entry, wordCenters, resampled, sequence, start, end, centers, keySize);
            scored.Add((entry, score));
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

    private static IEnumerable<WordEntry> EnumerateCandidates(
        WordList words,
        Point2 start,
        Point2 end,
        IReadOnlyList<char> observed,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        var startLetters = new HashSet<char> { observed[0] };
        var endLetters = new HashSet<char> { observed[^1] };
        foreach ((char letter, Point2 center) in centers)
        {
            if (center.DistanceTo(start) <= NearbyKeys * keySize)
            {
                startLetters.Add(letter);
            }

            if (center.DistanceTo(end) <= NearbyKeys * keySize)
            {
                endLetters.Add(letter);
            }
        }

        var yielded = new HashSet<string>();
        foreach (char startLetter in startLetters)
        {
            if (!words.ByFirstLetter.TryGetValue(startLetter, out List<WordEntry>? bucket))
            {
                continue;
            }

            int minLen = Math.Max(2, observed.Count - 4);
            int maxLen = observed.Count + 10;

            foreach (WordEntry entry in bucket)
            {
                // Length band keeps scoring cheap once the lexicon is ~100k words.
                int len = entry.Folded.Length;
                if (len < minLen || len > maxLen)
                {
                    continue;
                }

                if (!endLetters.Contains(entry.Folded[^1]))
                {
                    continue;
                }

                if (yielded.Add(new string(entry.Folded)))
                {
                    yield return entry;
                }
            }
        }
    }

        internal static double ScoreWord(
        WordEntry entry,
        List<Point2> wordCenters,
        Point2[] resampled,
        IReadOnlyList<char> observed,
        Point2 start,
        Point2 end,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        double seq = SpatialLevenshtein(observed, entry.Folded, centers, keySize);
        double dtw = Dtw(resampled, wordCenters) / keySize;
        double ordered = OrderedPathCost(resampled, wordCenters) / keySize;
        double first = start.DistanceTo(wordCenters[0]) / keySize;
        double last = end.DistanceTo(wordCenters[^1]) / keySize;
        double far = FarLetterPenalty(entry.Folded, resampled, centers, keySize);

        // Frequency is a light tie-break only — geometry dominates.
        double freq = 1.0 - entry.Frequency;

        return (1.50 * seq) + (1.10 * dtw) + (0.90 * ordered) + (0.45 * first) + (0.45 * last) + far + (0.06 * freq);
    }

    /// <summary>
    /// Snap each path sample to the nearest key and collapse consecutive duplicates.
    /// Samples farther than 1.4 keys from every key are skipped (gaps).
    /// </summary>
    public static IReadOnlyList<char> BuildObservedKeys(
        IReadOnlyList<Point2> samples,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        var observed = new List<char>();
        double maxDist = 0.72 * keySize;
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

    /// <summary>
    /// Edit distance where substituting two letters costs their key-center
    /// distance (so swapping M for N on AZERTY is expensive).
    /// </summary>
    internal static double SpatialLevenshtein(
        IReadOnlyList<char> observed,
        char[] word,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        int n = observed.Count;
        int m = word.Length;
        var dp = new double[n + 1, m + 1];
        const double gap = 0.62;

        for (int i = 0; i <= n; i++)
        {
            dp[i, 0] = i * gap;
        }

        for (int j = 0; j <= m; j++)
        {
            dp[0, j] = j * gap;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double sub = observed[i - 1] == word[j - 1]
                    ? 0
                    : SubstitutionCost(observed[i - 1], word[j - 1], centers, keySize);
                dp[i, j] = Math.Min(
                    dp[i - 1, j - 1] + sub,
                    Math.Min(dp[i - 1, j] + gap, dp[i, j - 1] + gap));
            }
        }

        return dp[n, m] / Math.Max(n, m);
    }

    private static double SubstitutionCost(
        char a, char b, IReadOnlyDictionary<char, Point2> centers, double keySize)
    {
        if (!centers.TryGetValue(a, out Point2 pa) || !centers.TryGetValue(b, out Point2 pb))
        {
            return 2.5;
        }

        return Math.Min(2.6, pa.DistanceTo(pb) / keySize);
    }

    /// <summary>
    /// Time-aligned key matching: each word letter is scored against a window of
    /// the path around its expected fraction. "content"'s N (letter 3 of 7) is
    /// compared to the third of the path — the M region on a comment-shaped swipe —
    /// instead of a later N the path happens to pass through.
    /// </summary>
    internal static double OrderedPathCost(IReadOnlyList<Point2> path, IReadOnlyList<Point2> wordCenters)
    {
        if (wordCenters.Count == 1)
        {
            return path.Min(p => p.DistanceTo(wordCenters[0]));
        }

        double cost = 0;
        int window = Math.Max(6, path.Count / Math.Max(2, wordCenters.Count));
        for (int j = 0; j < wordCenters.Count; j++)
        {
            double frac = (double)j / (wordCenters.Count - 1);
            int center = (int)Math.Round(frac * (path.Count - 1));
            int from = Math.Max(0, center - window);
            int to = Math.Min(path.Count - 1, center + window);
            double best = double.MaxValue;
            for (int i = from; i <= to; i++)
            {
                best = Math.Min(best, path[i].DistanceTo(wordCenters[j]));
            }

            cost += best;
        }

        return cost / wordCenters.Count;
    }

    private static double FarLetterPenalty(
        char[] word,
        Point2[] path,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        double penalty = 0;
        foreach (char letter in word.Distinct())
        {
            if (!centers.TryGetValue(letter, out Point2 center))
            {
                penalty += 2.0;
                continue;
            }

            double min = path.Min(p => p.DistanceTo(center));
            if (min > 1.85 * keySize)
            {
                penalty += (min / keySize) - 1.0;
            }
        }

        return penalty;
    }

    private static bool TryWordCenters(
        char[] folded, IReadOnlyDictionary<char, Point2> centers, out List<Point2> result)
    {
        result = new List<Point2>(folded.Length);
        foreach (char c in folded)
        {
            if (!centers.TryGetValue(c, out Point2 center))
            {
                result = [];
                return false;
            }

            result.Add(center);
        }

        return result.Count >= 2;
    }

    internal static double Dtw(IReadOnlyList<Point2> a, IReadOnlyList<Point2> b)
    {
        int n = a.Count;
        int m = b.Count;
        var dp = new double[n + 1, m + 1];
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                dp[i, j] = double.PositiveInfinity;
            }
        }

        dp[0, 0] = 0;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double cost = a[i - 1].DistanceTo(b[j - 1]);
                dp[i, j] = cost + Math.Min(dp[i - 1, j], Math.Min(dp[i, j - 1], dp[i - 1, j - 1]));
            }
        }

        return dp[n, m] / (n + m);
    }

    internal static Point2[] Resample(IReadOnlyList<Point2> points, int count)
    {
        var result = new Point2[count];
        if (points.Count == 0)
        {
            return result;
        }

        if (points.Count == 1)
        {
            Array.Fill(result, points[0]);
            return result;
        }

        double total = 0;
        for (int i = 1; i < points.Count; i++)
        {
            total += points[i - 1].DistanceTo(points[i]);
        }

        if (total <= 0)
        {
            Array.Fill(result, points[0]);
            return result;
        }

        double step = total / (count - 1);
        result[0] = points[0];
        result[count - 1] = points[^1];

        int segment = 0;
        double segmentStart = 0;
        double segmentLength = points[0].DistanceTo(points[1]);

        for (int i = 1; i < count - 1; i++)
        {
            double target = i * step;
            while (segment < points.Count - 2 && segmentStart + segmentLength < target)
            {
                segmentStart += segmentLength;
                segment++;
                segmentLength = points[segment].DistanceTo(points[segment + 1]);
            }

            double t = segmentLength <= 0 ? 0 : (target - segmentStart) / segmentLength;
            Point2 a = points[segment];
            Point2 b = points[segment + 1];
            result[i] = new Point2(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
        }

        return result;
    }
}
