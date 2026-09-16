namespace WinBoard.Core;

/// <summary>
/// Offline shape-writing decoder (SHARK2-inspired, no ML, no network).
///
/// Precision comes from the keys the pointer actually intended, not from a
/// loose nearest-key flyover + frequency:
///   1. Observed sequence = keys whose hit-rects were entered (preferred) or
///      samples snapped only when inside ~half a key pitch of a center.
///   2. Candidates must start/end on the keys nearest the path ends (tight
///      radius — not a 1.7-key halo that lets a neighbor row in).
///   3. Score is spatial: Levenshtein in key-space, ordered path fit, letters
///      far from the stroke, coverage of observed keys, length. Frequency is
///      a tiny tie-break and cannot rescue a geometric miss.
/// All distances are divided by <see cref="SwipeGeometry.KeyPitch"/> so layout
/// scale / DPI must not change the ranking of the same gesture.
/// </summary>
public static class SwipeDecoder
{
    private const int SampleCount = 64;
    private const double StartEndRadius = 0.58;
    private const double SnapRadius = 0.48;

    public static IReadOnlyList<string> Decode(
        IReadOnlyList<char> hitKeys,
        IReadOnlyList<Point2> path,
        IReadOnlyDictionary<char, Point2> centers,
        WordList words,
        double keySize,
        int maxResults = 5)
    {
        if (path.Count < 2 || centers.Count == 0)
        {
            return [];
        }

        double pitch = ResolvePitch(centers, keySize);
        Point2[] resampled = Resample(path, SampleCount);
        IReadOnlyList<char> snapped = BuildObservedKeys(resampled, centers, pitch);
        IReadOnlyList<char> sequence = hitKeys.Count >= 2 ? hitKeys : snapped;
        if (sequence.Count < 2)
        {
            return [];
        }

        Point2 start = resampled[0];
        Point2 end = resampled[^1];

        var scored = new List<(WordEntry Entry, double Score)>();
        foreach (WordEntry entry in EnumerateCandidates(words, start, end, sequence, centers, pitch))
        {
            if (!TryWordCenters(entry.Folded, centers, out List<Point2> wordCenters))
            {
                continue;
            }

            List<Point2> shape = CollapseConsecutive(wordCenters);
            double score = ScoreWord(
                entry, wordCenters, shape, resampled, sequence, start, end, centers, pitch);
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

    internal static double ResolvePitch(IReadOnlyDictionary<char, Point2> centers, double keySizeHint)
    {
        double pitch = SwipeGeometry.KeyPitch(centers);
        if (pitch <= 1 && keySizeHint > 1)
        {
            return keySizeHint;
        }

        return pitch;
    }

    private static IEnumerable<WordEntry> EnumerateCandidates(
        WordList words,
        Point2 start,
        Point2 end,
        IReadOnlyList<char> observed,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch)
    {
        HashSet<char> startLetters = LettersNear(start, observed[0], centers, pitch);
        HashSet<char> endLetters = LettersNear(end, observed[^1], centers, pitch);

        var yielded = new HashSet<string>();
        int minLen = Math.Max(2, observed.Count - 2);
        int maxLen = observed.Count + 3;

        foreach (char startLetter in startLetters)
        {
            if (!words.ByFirstLetter.TryGetValue(startLetter, out List<WordEntry>? bucket))
            {
                continue;
            }

            foreach (WordEntry entry in bucket)
            {
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

    private static HashSet<char> LettersNear(
        Point2 point, char observed, IReadOnlyDictionary<char, Point2> centers, double pitch)
    {
        var letters = new HashSet<char> { observed };
        double radius = StartEndRadius * pitch;
        foreach ((char letter, Point2 center) in centers)
        {
            if (center.DistanceTo(point) <= radius)
            {
                letters.Add(letter);
            }
        }

        return letters;
    }

    internal static double ScoreWord(
        WordEntry entry,
        List<Point2> wordCenters,
        Point2[] resampled,
        IReadOnlyList<char> observed,
        Point2 start,
        Point2 end,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch)
    {
        return ScoreWord(
            entry,
            wordCenters,
            CollapseConsecutive(wordCenters),
            resampled,
            observed,
            start,
            end,
            centers,
            pitch);
    }

    internal static double ScoreWord(
        WordEntry entry,
        List<Point2> wordCenters,
        List<Point2> shape,
        Point2[] resampled,
        IReadOnlyList<char> observed,
        Point2 start,
        Point2 end,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch)
    {
        double seq = SpatialLevenshtein(observed, entry.Folded, centers, pitch);
        double dtw = Dtw(resampled, shape) / pitch;
        double ordered = OrderedPathCost(resampled, shape) / pitch;
        double first = start.DistanceTo(wordCenters[0]) / pitch;
        double last = end.DistanceTo(wordCenters[^1]) / pitch;
        double far = FarLetterPenalty(entry.Folded, resampled, centers, pitch);
        double cover = CoveragePenalty(observed, entry.Folded);
        int collapsed = CollapseRuns(entry.Folded);
        double length = 0.55 * Math.Abs(collapsed - observed.Count);
        double freq = 0.04 * (1.0 - entry.Frequency);

        return (2.20 * seq) + (0.55 * dtw) + (1.15 * ordered) + (0.85 * first) + (0.85 * last)
            + far + cover + length + freq;
    }

    /// <summary>
    /// Snap each path sample to the nearest key only when it sits inside the
    /// key (half-pitch). Flyovers between keys are gaps, not extra letters.
    /// </summary>
    public static IReadOnlyList<char> BuildObservedKeys(
        IReadOnlyList<Point2> samples,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize)
    {
        double pitch = ResolvePitch(centers, keySize);
        var observed = new List<char>();
        double maxDist = SnapRadius * pitch;
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
        const double gap = 0.95;

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
                // Hit-testing collapses consecutive duplicates (hello → h,e,l,o).
                double insertLetter = j >= 2 && word[j - 1] == word[j - 2] ? 0 : gap;
                dp[i, j] = Math.Min(
                    dp[i - 1, j - 1] + sub,
                    Math.Min(dp[i - 1, j] + gap, dp[i, j - 1] + insertLetter));
            }
        }

        return dp[n, m] / Math.Max(n, m);
    }

    private static double SubstitutionCost(
        char a, char b, IReadOnlyDictionary<char, Point2> centers, double keySize)
    {
        if (!centers.TryGetValue(a, out Point2 pa) || !centers.TryGetValue(b, out Point2 pb))
        {
            return 3.0;
        }

        return Math.Min(3.2, Math.Max(0.85, pa.DistanceTo(pb) / keySize));
    }

    /// <summary>
    /// Each word letter is scored against a narrow window of the path around
    /// its expected fraction so a later accidental pass near another key does
    /// not count as that letter.
    /// </summary>
    internal static double OrderedPathCost(IReadOnlyList<Point2> path, IReadOnlyList<Point2> wordCenters)
    {
        if (wordCenters.Count == 1)
        {
            return path.Min(p => p.DistanceTo(wordCenters[0]));
        }

        double cost = 0;
        int window = Math.Max(2, path.Count / Math.Max(4, wordCenters.Count * 3));
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
        double pitch)
    {
        double penalty = 0;
        foreach (char letter in word.Distinct())
        {
            if (!centers.TryGetValue(letter, out Point2 center))
            {
                penalty += 3.0;
                continue;
            }

            double min = path.Min(p => p.DistanceTo(center));
            double keysAway = min / pitch;
            if (keysAway > 1.15)
            {
                penalty += (keysAway - 0.5) * 1.35;
            }
        }

        return penalty;
    }

    /// <summary>
    /// Observed keys that never appear in the candidate are almost always a
    /// wrong word (general — not a per-word ban).
    /// </summary>
    internal static double CoveragePenalty(IReadOnlyList<char> observed, char[] word)
    {
        var inWord = new HashSet<char>(word);
        int missing = 0;
        foreach (char c in observed.Distinct())
        {
            if (!inWord.Contains(c))
            {
                missing++;
            }
        }

        return 1.15 * missing;
    }

    private static List<Point2> CollapseConsecutive(List<Point2> centers)
    {
        var result = new List<Point2>(centers.Count);
        foreach (Point2 p in centers)
        {
            if (result.Count == 0 || result[^1].DistanceTo(p) > 0.01)
            {
                result.Add(p);
            }
        }

        return result.Count >= 2 ? result : centers;
    }

    private static int CollapseRuns(char[] word)
    {
        if (word.Length == 0)
        {
            return 0;
        }

        int n = 1;
        for (int i = 1; i < word.Length; i++)
        {
            if (word[i] != word[i - 1])
            {
                n++;
            }
        }

        return n;
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

        // Normalize by path length only so extra word letters cannot shrink DTW.
        return dp[n, m] / n;
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
