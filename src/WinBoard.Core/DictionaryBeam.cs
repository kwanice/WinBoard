namespace WinBoard.Core;

/// <summary>
/// Dictionary beam: walk the trie with spatial letter scores, union with
/// start/end bucket candidates, then rank with location + banded DTW + length
/// + graded hit-keys. Frequency / n-grams are applied later by
/// <see cref="SwipeDecoder"/>.
///
/// Tuning (radii in key pitches; adjacent keys sit at ~1.0):
///   LocationWeight 1.05 — corresponding-point distance, primary channel so a
///     short path through M cannot match a content/collent template.
///   DtwWeight 0.45 / BandFraction 0.12 — modest speed warp only.
///   AnchorWeight 2.8 — start/end key centers vs path caps; far first/last
///     letters lose hard (hello vs jello).
///   HitKeyWeight 5.5 — quadratic miss when a <b>center</b> hit is neither in
///     the word nor a short-segment flyover (M vs L ≈ 1.7). Graze extras on a
///     messy path are capped so they cannot elect a longer covering word.
///   SoftHitWeight 2.2 / SoftHitRadius 0.62 — near-miss mid-path OK.
///   HitBoost / HitBoostCap — coverage bonus, not per-key stacking.
///   Length* — crush 12-letter rivals on a ~7-key gesture; uses simplified
///     path length so finger wander does not inflate the budget.
/// </summary>
public static class DictionaryBeam
{
    public const int Width = 48;

    public const int MaxDtw = 700;

    /// <summary>Absolute corresponding-point distance (primary spatial channel).</summary>
    public const double LocationWeight = 1.05;

    /// <summary>Banded DTW; secondary so speed variation does not hide extra loops.</summary>
    public const double DtwWeight = 0.45;

    /// <summary>Quadratic weight on start/end key-to-cap distance (above AnchorInner).</summary>
    public const double AnchorWeight = 2.8;

    /// <summary>Inside this (pitches), the start/end cap sits on the key — free.</summary>
    public const double AnchorInner = 0.30;

    /// <summary>Hard prune when first/last key is this far and was not hit-tested.</summary>
    public const double AnchorReject = 0.78;

    public const double LengthRatioLong = 1.08;

    public const double LengthRatioLongWeight = 10.0;

    public const double LengthRatioShort = 0.55;

    public const double LengthRatioShortWeight = 1.0;

    public const double LengthRatioHardReject = 1.18;

    public const int LengthGateMinLetters = 10;

    public const int LetterCountMinHits = 3;

    public const int LetterCountSlack = 2;

    public const double LetterCountLongRatio = 1.30;

    public const double LetterCountWeight = 8.0;

    /// <summary>
    /// Graded cost for a clearly entered key that is neither in the word nor a
    /// flyover. A 1-pitch neighbor miss (M vs L) is ~1.7.
    /// </summary>
    public const double HitKeyWeight = 5.5;

    /// <summary>Softer weight when the path entered the key but missed the center.</summary>
    public const double MidHitWeight = 3.6;

    /// <summary>Softer weight when the path only grazed the key (not entered).</summary>
    public const double SoftHitWeight = 2.2;

    /// <summary>Per matched word-letter contribution toward the coverage bonus.</summary>
    public const double HitBoost = 0.18;

    /// <summary>Max |boost| so a longer word cannot stack HitBoost on wander keys.</summary>
    public const double HitBoostCap = 0.42;

    /// <summary>
    /// Cap on summed unmatched-hit penalties. One clear M-vs-L miss (~1.7) still
    /// fits; a 15-key scribble cannot bury the location channel.
    /// </summary>
    public const double HitMissCap = 1.35;

    public const double FlyoverRadius = 0.45;

    /// <summary>Looser flyover for mid-path near-misses (not center crossings).</summary>
    public const double MidFlyoverRadius = 0.52;

    /// <summary>
    /// Template chords longer than this (pitches) do not create a flyover
    /// corridor — only vertices (word keys) and short hops do. Stops m→a
    /// from excusing the whole board.
    /// </summary>
    public const double FlyoverMaxSegment = 1.58;

    /// <summary>Word letter whose center never comes this close to the stroke (pitches).</summary>
    public const double MissingLetterRadius = 0.88;

    public const double MissingLetterWeight = 0.72;

    public const double FrequencyTieBreak = 0.012;

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
            double templateLength = SwipePath.Length(templateLine);
            double rest = LengthRatioPenalty(templateLength, gesture.SimplifiedLength, gesture.Pitch)
                + LetterCountPenalty(entry.Folded.Length, hitCount)
                + SoftHitCost(gesture, entry.Folded, templateLine, centers)
                + AnchorCost(gesture, centersLine[0], centersLine[^1])
                + (FrequencyTieBreak * (1.0 - entry.Frequency));
            double location = SwipePath.MeanPairwise(gesture.Samples, template) / gesture.Pitch;
            double locPart = LocationWeight * location;

            int band = BandedDtw.BandWidth(gesture.Samples.Length);
            double lb = BandedDtw.LowerBound(gesture.Samples, template, gesture.Pitch, band);
            if (locPart + (DtwWeight * lb) + rest > best + 0.85)
            {
                continue;
            }

            double abandonMean = double.IsPositiveInfinity(best)
                ? 1e9
                : (best + 0.85 - locPart - rest) / Math.Max(DtwWeight, 1e-6);
            // Distance() compares against the cumulative (not mean) cost.
            double abandon = Math.Max(lb, abandonMean) * gesture.Samples.Length;
            double dtw = BandedDtw.Distance(gesture.Samples, template, gesture.Pitch, abandon);
            if (double.IsPositiveInfinity(dtw))
            {
                continue;
            }

            double score = locPart + (DtwWeight * dtw) + rest;

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

    /// <summary>
    /// Recompute weighted score parts for diagnostics. Ranking stays in
    /// <see cref="Search"/>; this does not change weights or abandon.
    /// </summary>
    internal static SwipeScoreBreakdown? Describe(
        EncodedGesture gesture,
        WordEntry entry,
        IReadOnlyDictionary<char, Point2> centers)
    {
        if (!SwipePath.TryWordCenters(entry.Folded, centers, out List<Point2> centersLine))
        {
            return null;
        }

        List<Point2> templateLine = SwipePath.CollapseConsecutive(centersLine);
        Point2[] template = SwipePath.Resample(templateLine, gesture.Samples.Length);
        double templateLength = SwipePath.Length(templateLine);
        int hitCount = CollapsedHitCount(gesture.HitKeys);
        double lengthPart = LengthRatioPenalty(templateLength, gesture.SimplifiedLength, gesture.Pitch)
            + LetterCountPenalty(entry.Folded.Length, hitCount);
        double hitPart = SoftHitCost(gesture, entry.Folded, templateLine, centers);
        double anchorPart = AnchorCost(gesture, centersLine[0], centersLine[^1]);
        double location = SwipePath.MeanPairwise(gesture.Samples, template) / gesture.Pitch;
        double locPart = LocationWeight * location;
        double dtw = BandedDtw.Distance(
            gesture.Samples,
            template,
            gesture.Pitch,
            double.PositiveInfinity);
        if (double.IsPositiveInfinity(dtw))
        {
            return null;
        }

        double dtwPart = DtwWeight * dtw;
        return new SwipeScoreBreakdown
        {
            Spatial = locPart + dtwPart,
            Dtw = dtwPart,
            Location = locPart,
            Length = lengthPart,
            Anchors = anchorPart,
            HitKeys = hitPart,
            Language = 0,
        };
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

    internal static double AnchorCost(EncodedGesture gesture, Point2 wordStart, Point2 wordEnd)
    {
        Point2 pathStart = gesture.Samples[0];
        Point2 pathEnd = gesture.Samples[^1];
        double ds = Math.Max(0, (pathStart.DistanceTo(wordStart) / gesture.Pitch) - AnchorInner);
        double de = Math.Max(0, (pathEnd.DistanceTo(wordEnd) / gesture.Pitch) - AnchorInner);
        return AnchorWeight * ((ds * ds) + (de * de));
    }

    internal static bool AnchorRejects(EncodedGesture gesture, char wordStart, char wordEnd, Point2 startCenter, Point2 endCenter)
    {
        double ds = gesture.Samples[0].DistanceTo(startCenter) / gesture.Pitch;
        double de = gesture.Samples[^1].DistanceTo(endCenter) / gesture.Pitch;
        char hitStart = gesture.HitKeys.Count > 0 ? char.ToLowerInvariant(gesture.HitKeys[0]) : '\0';
        char hitEnd = gesture.HitKeys.Count > 0 ? char.ToLowerInvariant(gesture.HitKeys[^1]) : '\0';
        if (ds > AnchorReject && wordStart != hitStart)
        {
            return true;
        }

        if (de > AnchorReject && wordEnd != hitEnd)
        {
            return true;
        }

        return false;
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
            if (!gesture.StartLetters.Contains(start.Letter))
            {
                continue;
            }

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

        if (AnchorRejects(gesture, entry.Folded[0], last, wordCenters[0], wordCenters[^1]))
        {
            return;
        }

        double templateLength = SwipePath.Length(SwipePath.CollapseConsecutive(wordCenters));
        if (LengthRatioRejects(templateLength, gesture.SimplifiedLength, entry.Folded.Length, gesture.Pitch))
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

            double cheap = LengthRatioPenalty(templateLength, gesture.SimplifiedLength, gesture.Pitch)
                + LetterCountPenalty(entry.Folded.Length, hitCount)
                + AnchorCost(gesture, wordCenters[0], wordCenters[^1])
                + ((1.0 - ((double)lcs / Math.Max(1, need))) * 1.4)
                + ((1.0 - entry.Frequency) * 0.10);
            pool.Add((entry, cheap));
            return;
        }

        pool.Add((entry, AnchorCost(gesture, wordCenters[0], wordCenters[^1]) + ((1.0 - entry.Frequency) * 0.2)));
    }

    internal static double SoftHitCost(
        EncodedGesture gesture,
        char[] word,
        IReadOnlyList<Point2> templateLine,
        IReadOnlyDictionary<char, Point2> centers)
    {
        var inWord = new HashSet<char>(word);
        var seen = new HashSet<char>();
        int matched = 0;
        int matchedCenter = 0;
        var missCosts = new List<double>();

        foreach (char raw in gesture.HitKeys)
        {
            char hit = char.ToLowerInvariant(raw);
            if (!seen.Add(hit))
            {
                continue;
            }

            if (inWord.Contains(hit))
            {
                matched++;
                if (gesture.CenterHits.Contains(hit))
                {
                    matchedCenter++;
                }

                continue;
            }

            if (!centers.TryGetValue(hit, out Point2 center))
            {
                missCosts.Add(gesture.CenterHits.Contains(hit) ? HitKeyWeight : MidHitWeight);
                continue;
            }

            bool centerHit = gesture.CenterHits.Contains(hit);
            double fly = centerHit ? FlyoverRadius : MidFlyoverRadius;
            double weight = centerHit ? HitKeyWeight : MidHitWeight;
            double d = MinFlyoverDistance(center, templateLine, gesture.Pitch) / gesture.Pitch;
            if (d > fly)
            {
                double excess = d - fly;
                missCosts.Add(weight * excess * excess);
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

            double d = MinFlyoverDistance(center, templateLine, gesture.Pitch) / gesture.Pitch;
            if (d > MidFlyoverRadius)
            {
                double excess = d - MidFlyoverRadius;
                missCosts.Add(SoftHitWeight * excess * excess);
            }
        }

        double coverage = inWord.Count == 0 ? 0 : (double)matched / inWord.Count;
        double boost = -HitBoostCap * coverage;
        if (matchedCenter > 0 && matched > 0)
        {
            boost -= 0.08 * ((double)matchedCenter / matched);
        }

        boost = Math.Max(boost, -HitBoostCap - 0.08);

        missCosts.Sort((a, b) => b.CompareTo(a));
        double miss = 0;
        int take = Math.Min(missCosts.Count, 4);
        for (int i = 0; i < take; i++)
        {
            miss += missCosts[i];
        }

        if (miss > HitMissCap)
        {
            miss = HitMissCap;
        }

        return boost + miss + MissingLetterCost(gesture, inWord, centers);
    }

    internal static double MissingLetterCost(
        EncodedGesture gesture,
        HashSet<char> inWord,
        IReadOnlyDictionary<char, Point2> centers)
    {
        double penalty = 0;
        var hitSet = new HashSet<char>();
        foreach (char raw in gesture.HitKeys)
        {
            hitSet.Add(char.ToLowerInvariant(raw));
        }

        foreach (char letter in inWord)
        {
            if (hitSet.Contains(letter) || gesture.SoftHits.Contains(letter))
            {
                continue;
            }

            if (!centers.TryGetValue(letter, out Point2 center))
            {
                penalty += MissingLetterWeight;
                continue;
            }

            double d = MinDistanceToSamples(center, gesture.Samples) / gesture.Pitch;
            if (d > MissingLetterRadius)
            {
                double excess = d - MissingLetterRadius;
                penalty += MissingLetterWeight * (0.55 + (excess * excess));
            }
        }

        return penalty;
    }

    /// <summary>
    /// Distance to the template for flyover tests: word-key vertices always
    /// count; chords count only when they are short (adjacent / one-row hop).
    /// </summary>
    internal static double MinFlyoverDistance(
        Point2 point, IReadOnlyList<Point2> line, double pitch)
    {
        if (line.Count == 0)
        {
            return double.PositiveInfinity;
        }

        double min = double.PositiveInfinity;
        for (int i = 0; i < line.Count; i++)
        {
            min = Math.Min(min, point.DistanceTo(line[i]));
        }

        if (line.Count == 1)
        {
            return min;
        }

        double maxSeg = FlyoverMaxSegment * pitch;
        for (int i = 1; i < line.Count; i++)
        {
            double seg = line[i - 1].DistanceTo(line[i]);
            if (seg > maxSeg)
            {
                continue;
            }

            min = Math.Min(min, point.DistanceToSegment(line[i - 1], line[i]));
        }

        return min;
    }

    private static double MinDistanceToSamples(Point2 point, Point2[] samples)
    {
        double min = double.PositiveInfinity;
        foreach (Point2 sample in samples)
        {
            min = Math.Min(min, point.DistanceTo(sample));
        }

        return min;
    }
}
