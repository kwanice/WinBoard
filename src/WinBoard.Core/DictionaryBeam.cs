namespace WinBoard.Core;

/// <summary>
/// Dictionary beam: walk the trie with spatial letter scores, union with
/// start/end bucket candidates, then rank with banded DTW + length + soft hits.
/// Frequency / n-grams are applied later by <see cref="SwipeDecoder"/>.
/// </summary>
public static class DictionaryBeam
{
    public const int Width = 48;

    public const int MaxDtw = 700;

    public const double LengthRatioLong = 1.12;

    public const double LengthRatioLongWeight = 8.0;

    public const double LengthRatioShort = 0.55;

    public const double LengthRatioShortWeight = 1.0;

    public const double LengthRatioHardReject = 1.28;

    public const int LengthGateMinLetters = 10;

    public const int LetterCountMinHits = 4;

    public const int LetterCountSlack = 2;

    public const double LetterCountLongRatio = 1.35;

    public const double LetterCountWeight = 8.0;

    public const double SoftHitWeight = 1.35;

    public const double HitBoost = 0.05;

    public const double FlyoverRadius = 0.50;

    public const double FrequencyTieBreak = 0.04;

    public const double LcsMinRatio = 0.38;

    public static List<(WordEntry Entry, double Score)> Search(
        EncodedGesture gesture,
        WordList words,
        IReadOnlyDictionary<char, Point2> centers)
    {
        var seen = new HashSet<string>();
        var pool = new List<(WordEntry Entry, double Cheap)>();
        int hitCount = CollapsedHitCount(gesture.HitKeys);

        CollectFromTrie(gesture, words.Trie, seen, pool, hitCount, centers);
        CollectFromBuckets(gesture, words, seen, pool, hitCount, centers);

        pool.Sort((a, b) => a.Cheap.CompareTo(b.Cheap));
        int dtwCap = Math.Min(pool.Count, MaxDtw);
        var scored = new List<(WordEntry Entry, double Score)>(dtwCap);
        double best = double.PositiveInfinity;

        for (int i = 0; i < dtwCap; i++)
        {
            WordEntry entry = pool[i].Entry;
            if (!SwipePath.TryWordCenters(entry.Folded, centers, out List<Point2> centersLine))
            {
                continue;
            }

            List<Point2> templateLine = SwipePath.CollapseConsecutive(centersLine);
            Point2[] template = SwipePath.Resample(templateLine, gesture.Samples.Length);
            int band = BandedDtw.BandWidth(gesture.Samples.Length);
            double lb = BandedDtw.LowerBound(gesture.Samples, template, gesture.Pitch, band);
            if (lb > best + 0.85)
            {
                continue;
            }

            double abandon = double.IsPositiveInfinity(best) ? 1e9 : best + 0.85;
            double dtw = BandedDtw.Distance(gesture.Samples, template, gesture.Pitch, abandon);
            if (double.IsPositiveInfinity(dtw))
            {
                continue;
            }

            double templateLength = SwipePath.Length(templateLine);
            double score = dtw
                + LengthRatioPenalty(templateLength, gesture.Length, gesture.Pitch)
                + LetterCountPenalty(entry.Folded.Length, hitCount)
                + SoftHitCost(gesture, entry.Folded, templateLine, centers)
                + (FrequencyTieBreak * (1.0 - entry.Frequency));

            scored.Add((entry, score));
            if (score < best)
            {
                best = score;
            }
        }

        scored.Sort((a, b) => a.Score.CompareTo(b.Score));
        if (scored.Count > Width)
        {
            scored.RemoveRange(Width, scored.Count - Width);
        }

        return scored;
    }

    internal static bool LetterCountRejects(int wordLetters, int hitCount)
    {
        if (hitCount < LetterCountMinHits || wordLetters < LengthGateMinLetters)
        {
            return false;
        }

        return wordLetters > hitCount + LetterCountSlack
            && wordLetters > hitCount * LetterCountLongRatio;
    }

    internal static double LetterCountPenalty(int wordLetters, int hitCount)
    {
        if (hitCount < LetterCountMinHits)
        {
            return 0;
        }

        int budget = hitCount + LetterCountSlack;
        if (wordLetters <= budget)
        {
            return 0;
        }

        double extra = wordLetters - budget;
        return LetterCountWeight * extra * extra;
    }

    internal static bool LengthRatioRejects(
        double templateLength, double userLength, int wordLetters, double pitch)
    {
        if (wordLetters < LengthGateMinLetters)
        {
            return false;
        }

        double u = Math.Max(userLength, pitch * 0.5);
        return templateLength / u > LengthRatioHardReject;
    }

    internal static double LengthRatioPenalty(double templateLength, double userLength, double pitch)
    {
        double u = Math.Max(userLength, pitch * 0.5);
        double ratio = templateLength / u;
        if (ratio > LengthRatioLong)
        {
            double excess = ratio - LengthRatioLong;
            return LengthRatioLongWeight * excess * excess;
        }

        if (ratio < LengthRatioShort)
        {
            double excess = LengthRatioShort - ratio;
            return LengthRatioShortWeight * excess * excess;
        }

        return 0;
    }

    internal static int CollapsedHitCount(IReadOnlyList<char> hitKeys)
    {
        int count = 0;
        char last = '\0';
        foreach (char raw in hitKeys)
        {
            char hit = char.ToLowerInvariant(raw);
            if (count == 0 || hit != last)
            {
                count++;
                last = hit;
            }
        }

        return count;
    }

    internal static int Lcs(char[] a, char[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                cur[j] = a[i - 1] == b[j - 1]
                    ? prev[j - 1] + 1
                    : Math.Max(prev[j], cur[j - 1]);
            }

            (prev, cur) = (cur, prev);
            Array.Clear(cur);
        }

        return prev[b.Length];
    }

    private static void CollectFromBuckets(
        EncodedGesture gesture,
        WordList words,
        HashSet<string> seen,
        List<(WordEntry Entry, double Cheap)> pool,
        int hitCount,
        IReadOnlyDictionary<char, Point2> centers)
    {
        foreach (char start in gesture.StartLetters)
        {
            if (!words.ByFirstLetter.TryGetValue(start, out List<WordEntry>? bucket))
            {
                continue;
            }

            foreach (WordEntry entry in bucket)
            {
                Consider(entry, gesture, seen, pool, hitCount, centers);
            }
        }
    }

    private static void CollectFromTrie(
        EncodedGesture gesture,
        WordTrie trie,
        HashSet<string> seen,
        List<(WordEntry Entry, double Cheap)> pool,
        int hitCount,
        IReadOnlyDictionary<char, Point2> centers)
    {
        SpatialFrame[] frames = gesture.Frames;
        if (frames.Length == 0)
        {
            return;
        }

        var beam = new List<(WordTrie.Node Node, int Frame, char Last, double Cost)>(Width * 2);
        foreach (LetterScore start in frames[0].Top)
        {
            if (trie.Root.Next.TryGetValue(start.Letter, out WordTrie.Node? child))
            {
                beam.Add((child, 0, start.Letter, 1.0 - start.Score));
            }
        }

        for (int f = 1; f < frames.Length && beam.Count > 0; f++)
        {
            var next = new List<(WordTrie.Node Node, int Frame, char Last, double Cost)>(Width * 3);
            LetterScore[] top = frames[f].Top;
            foreach ((WordTrie.Node node, int _, char last, double cost) in beam)
            {
                double stay = StayCost(last, top);
                next.Add((node, f, last, cost + stay));
                foreach (LetterScore ls in top)
                {
                    if (ls.Letter == last)
                    {
                        continue;
                    }

                    if (node.Next.TryGetValue(ls.Letter, out WordTrie.Node? child))
                    {
                        next.Add((child, f, ls.Letter, cost + (1.0 - ls.Score)));
                    }
                }
            }

            next.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            if (next.Count > Width)
            {
                next.RemoveRange(Width, next.Count - Width);
            }

            beam = next;
        }

        foreach ((WordTrie.Node node, int _, char _, double _) in beam)
        {
            CollectEnds(node, gesture, seen, pool, hitCount, centers, depth: 0);
        }
    }

    private static void CollectEnds(
        WordTrie.Node node,
        EncodedGesture gesture,
        HashSet<string> seen,
        List<(WordEntry Entry, double Cheap)> pool,
        int hitCount,
        IReadOnlyDictionary<char, Point2> centers,
        int depth)
    {
        if (node.Ends is { Count: > 0 })
        {
            foreach (WordEntry entry in node.Ends)
            {
                Consider(entry, gesture, seen, pool, hitCount, centers);
            }
        }

        if (depth >= 2)
        {
            return;
        }

        foreach (WordTrie.Node child in node.Next.Values)
        {
            CollectEnds(child, gesture, seen, pool, hitCount, centers, depth + 1);
        }
    }

    private static double StayCost(char last, LetterScore[] top)
    {
        foreach (LetterScore ls in top)
        {
            if (ls.Letter == last)
            {
                return 0.15 * (1.0 - ls.Score);
            }
        }

        return 0.55;
    }

    private static void Consider(
        WordEntry entry,
        EncodedGesture gesture,
        HashSet<string> seen,
        List<(WordEntry Entry, double Cheap)> pool,
        int hitCount,
        IReadOnlyDictionary<char, Point2> centers)
    {
        char last = entry.Folded[^1];
        if (!gesture.EndLetters.Contains(last) || !gesture.StartLetters.Contains(entry.Folded[0]))
        {
            return;
        }

        if (!seen.Add(new string(entry.Folded)))
        {
            return;
        }

        if (LetterCountRejects(entry.Folded.Length, hitCount))
        {
            return;
        }

        if (!SwipePath.TryWordCenters(entry.Folded, centers, out List<Point2> wordCenters))
        {
            return;
        }

        double templateLength = SwipePath.Length(SwipePath.CollapseConsecutive(wordCenters));
        if (LengthRatioRejects(templateLength, gesture.Length, entry.Folded.Length, gesture.Pitch))
        {
            return;
        }

        if (gesture.Canonical.Length >= 2)
        {
            int lcs = Lcs(entry.Folded, gesture.Canonical);
            int need = Math.Min(entry.Folded.Length, gesture.Canonical.Length);
            if (lcs < need * LcsMinRatio && lcs < entry.Folded.Length - 4)
            {
                return;
            }

            double cheap = LengthRatioPenalty(templateLength, gesture.Length, gesture.Pitch)
                + LetterCountPenalty(entry.Folded.Length, hitCount)
                + ((1.0 - ((double)lcs / Math.Max(1, need))) * 1.4)
                + ((1.0 - entry.Frequency) * 0.15);
            pool.Add((entry, cheap));
            return;
        }

        pool.Add((entry, (1.0 - entry.Frequency) * 0.2));
    }

    internal static double SoftHitCost(
        EncodedGesture gesture,
        char[] word,
        IReadOnlyList<Point2> templateLine,
        IReadOnlyDictionary<char, Point2> centers)
    {
        var inWord = new HashSet<char>(word);
        double penalty = 0;
        var seen = new HashSet<char>();
        foreach (char raw in gesture.HitKeys)
        {
            char hit = char.ToLowerInvariant(raw);
            if (!seen.Add(hit))
            {
                continue;
            }

            if (inWord.Contains(hit))
            {
                penalty -= HitBoost;
                continue;
            }

            if (!centers.TryGetValue(hit, out Point2 center))
            {
                continue;
            }

            double d = MinDistanceToPolyline(center, templateLine) / gesture.Pitch;
            if (d > FlyoverRadius)
            {
                double excess = d - FlyoverRadius;
                penalty += SoftHitWeight * excess * excess;
            }
        }

        foreach (char soft in gesture.SoftHits)
        {
            if (!seen.Add(soft) || inWord.Contains(soft))
            {
                continue;
            }

            if (!centers.TryGetValue(soft, out Point2 center))
            {
                continue;
            }

            double d = MinDistanceToPolyline(center, templateLine) / gesture.Pitch;
            if (d > FlyoverRadius)
            {
                double excess = d - FlyoverRadius;
                penalty += 0.45 * SoftHitWeight * excess * excess;
            }
        }

        return penalty;
    }

    private static double MinDistanceToPolyline(Point2 point, IReadOnlyList<Point2> line)
    {
        if (line.Count == 0)
        {
            return double.PositiveInfinity;
        }

        if (line.Count == 1)
        {
            return point.DistanceTo(line[0]);
        }

        double min = double.PositiveInfinity;
        for (int i = 1; i < line.Count; i++)
        {
            min = Math.Min(min, point.DistanceToSegment(line[i - 1], line[i]));
        }

        return min;
    }
}
