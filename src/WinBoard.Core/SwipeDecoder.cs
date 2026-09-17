namespace WinBoard.Core;

/// <summary>
/// Offline SHARK2-style shape-writing decoder (Kristensson &amp; Zhai, UIST 2004).
/// Local only: no ML, no network. No per-word blacklists.
///
/// For each dictionary word an <em>ideal polyline</em> is built through the
/// current layout's key centers, then both the user stroke and that template
/// are uniformly resampled to <see cref="SampleCount"/> equidistant points.
/// Candidates whose first/last keys are not near the stroke ends are pruned.
/// Ranking is a <b>location-weighted</b> SHARK2 blend:
///   • shape — translation + uniform scale (bbox/centroid), mean point distance;
///   • location — absolute keyboard coordinates (corresponding points);
///   • tunnel / skip — every template key center must lie near the stroke;
///   • hit-keys — letters the pointer entered (or grazed within a small
///     neighbor radius) are a <em>strong soft</em> constraint: a miss that is
///     not a flyover along the template costs a graded penalty, not a cliff;
///   • length — letter-count vs hit-key count and template vs stroke length
///     <b>outrank</b> the language prior (12–16 letter words on a ~7-key
///     gesture are crushed);
///   • language — compact unigram/bigram rescore of the top spatial pool.
/// Distances are in <see cref="SwipeGeometry.KeyPitch"/> units so layout scale
/// / DPI must not change the ranking of the same gesture.
/// </summary>
public static class SwipeDecoder
{
    internal const int SampleCount = 64;

    // --- Tuning (radii in key pitches; adjacent keys sit at ~1.0) ----------
    //
    // StartEndRadius 0.68 (was 0.48 in 0.7.4): mm-scale / fraction-of-pitch
    //   halo so an off-center cap does not prune the intended word.
    // HitKeyStartEndRadius 0.84: hit-tested end letter may sit on the key
    //   edge (~0.5) plus a small graze.
    // ShapeWeight*ShapeScale vs LocationWeight: location still dominates.
    // CoverageRadius 0.58: slightly more forgiving skip than 0.7.4.
    // Hit-keys are graded (HitKeyWeight * excess²), not an 8.0 cliff.
    //   SoftHitRadius lets a path sample near M count even if the rect was
    //   missed by a millimetre. A clear M hit still beats an L-only neighbor.
    // LengthRatio* / LetterCount*: length outranks language.
    // LanguageWeight lives on LanguageModel (0.55). FrequencyTieBreak stays a
    //   last-ditch spatial tie-break when no language model is passed.

    /// <summary>Start/end gate. Adjacent key centers are ~1.0 pitches away.</summary>
    internal const double StartEndRadius = 0.68;

    /// <summary>Looser only for the letter hit-tested at the start/end cap.</summary>
    internal const double HitKeyStartEndRadius = 0.84;

    /// <summary>Secondary. Pure shape confuses same-length keyboard neighbors.</summary>
    internal const double ShapeWeight = 0.32;

    /// <summary>Maps bbox-normalized shape distance into roughly key-pitch units.</summary>
    internal const double ShapeScale = 2.2;

    /// <summary>Absolute corresponding-point distance (primary channel).</summary>
    internal const double LocationWeight = 0.90;

    /// <summary>Mean key-to-path distance; skip penalty below is the sharp tool.</summary>
    internal const double TunnelWeight = 0.15;

    /// <summary>Inside this, an intermediate key counts as visited.</summary>
    internal const double CoverageRadius = 0.58;

    /// <summary>Quadratic weight on (distance − CoverageRadius) for skipped keys.</summary>
    internal const double SkipExcessWeight = 3.6;

    /// <summary>Template / user length above this is “too long”.</summary>
    internal const double LengthRatioLong = 1.10;

    internal const double LengthRatioLongWeight = 8.0;

    /// <summary>Template / user length below this is “too short” (user loops are OK).</summary>
    internal const double LengthRatioShort = 0.58;

    internal const double LengthRatioShortWeight = 1.0;

    /// <summary>
    /// Hard prune when a long word’s template is this much longer than the stroke.
    /// </summary>
    internal const double LengthRatioHardReject = 1.28;

    /// <summary>Minimum word length (letters) before the hard length prune applies.</summary>
    internal const int LengthGateMinLetters = 10;

    /// <summary>Need this many collapsed hit keys before letter-count gating.</summary>
    internal const int LetterCountMinHits = 4;

    /// <summary>word.Length may exceed hit-key count by this many letters.</summary>
    internal const int LetterCountSlack = 2;

    /// <summary>Also reject when word.Length &gt; hitCount × this ratio.</summary>
    internal const double LetterCountLongRatio = 1.35;

    /// <summary>Quadratic letter-count miss; larger than LanguageWeight (0.55).</summary>
    internal const double LetterCountWeight = 8.0;

    /// <summary>
    /// Graded cost for a hit-key that is neither in the word nor a flyover.
    /// A 1-pitch neighbor miss (M vs L) is ~1.7, enough to keep M-words ahead
    /// of L-only neighbors without an 8.0 cliff that dies on a 1 mm graze.
    /// </summary>
    internal const double HitKeyWeight = 5.5;

    /// <summary>Softer weight when the path only grazed the key (not entered).</summary>
    internal const double HitKeySoftWeight = 2.8;

    /// <summary>
    /// A hit whose center sits this close (pitches) to a template segment is a
    /// flyover along the word, not a required letter.
    /// </summary>
    internal const double HitFlyoverRadius = 0.45;

    /// <summary>
    /// Path samples this close to a key center count as a soft hit even if the
    /// inset rect was missed (mm-scale / ~0.4 of a pitch).
    /// </summary>
    internal const double SoftHitRadius = 0.56;

    internal const double FrequencyTieBreak = 0.012;

    public static IReadOnlyList<string> Decode(
        IReadOnlyList<char> hitKeys,
        IReadOnlyList<Point2> path,
        IReadOnlyDictionary<char, Point2> centers,
        WordList words,
        double keySize,
        int maxResults = 5,
        string? previousWord = null,
        LanguageModel? language = null)
    {
        if (path.Count < 2 || centers.Count == 0)
        {
            return [];
        }

        double pitch = ResolvePitch(centers, keySize);
        Point2[] user = Resample(path, SampleCount);
        Point2 start = user[0];
        Point2 end = user[^1];
        double userLength = PolylineLength(user);
        char hitStart = hitKeys.Count > 0 ? char.ToLowerInvariant(hitKeys[0]) : '\0';
        char hitEnd = hitKeys.Count > 0 ? char.ToLowerInvariant(hitKeys[^1]) : '\0';
        int hitCount = CollapsedHitCount(hitKeys);

        HashSet<char> startLetters = LettersNear(start, centers, pitch, StartEndRadius);
        HashSet<char> endLetters = LettersNear(end, centers, pitch, StartEndRadius);
        if (hitStart != '\0')
        {
            startLetters.Add(hitStart);
        }

        if (hitEnd != '\0')
        {
            endLetters.Add(hitEnd);
        }

        if (startLetters.Count == 0 || endLetters.Count == 0)
        {
            return [];
        }

        Point2[] userShape = NormalizeShape(user);
        var scored = new List<(WordEntry Entry, double Score)>();

        foreach (WordEntry entry in EnumerateCandidates(
            words, startLetters, endLetters, start, end, hitStart, hitEnd, centers, pitch))
        {
            if (!TryWordCenters(entry.Folded, centers, out List<Point2> wordCenters))
            {
                continue;
            }

            List<Point2> templateLine = CollapseConsecutive(wordCenters);
            double templateLength = PolylineLength(templateLine);
            if (LetterCountRejects(entry.Folded.Length, hitCount)
                || LengthRatioRejects(templateLength, userLength, entry.Folded.Length, pitch))
            {
                continue;
            }

            Point2[] template = Resample(templateLine, SampleCount);
            double score = ScoreChannels(
                user,
                userShape,
                userLength,
                template,
                templateLine,
                entry.Frequency,
                pitch);
            score += LengthRatioPenalty(templateLength, userLength, pitch);
            score += LetterCountPenalty(entry.Folded.Length, hitCount);
            score += HitKeyConstraint(hitKeys, entry.Folded, templateLine, centers, pitch, user);
            scored.Add((entry, score));
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
    /// Rescore the spatial pool with P(w|prev). Length already pruned or
    /// penalized the long-word tail, so language cannot revive it.
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

    internal static double ResolvePitch(IReadOnlyDictionary<char, Point2> centers, double keySizeHint)
    {
        double pitch = SwipeGeometry.KeyPitch(centers);
        if (pitch <= 1 && keySizeHint > 1)
        {
            return keySizeHint;
        }

        return pitch;
    }

    internal static double ScoreChannels(
        Point2[] user,
        Point2[] userShape,
        double userLength,
        Point2[] template,
        IReadOnlyList<Point2> templateLine,
        double frequency,
        double pitch)
    {
        Point2[] templateShape = NormalizeShape(template);
        double shape = MeanPairwise(userShape, templateShape);
        double location = MeanPairwise(user, template) / pitch;
        double tunnel = KeyTunnel(templateLine, user) / pitch;
        double skip = SkippedKeyPenalty(templateLine, user, pitch);
        double freq = FrequencyTieBreak * (1.0 - frequency);
        return (ShapeWeight * ShapeScale * shape)
            + (LocationWeight * location)
            + (TunnelWeight * tunnel)
            + skip
            + freq;
    }

    /// <summary>
    /// Translation (centroid) + uniform scale (bbox diagonal). Aspect ratio is
    /// preserved — that <em>is</em> the proportional shape SHARK2 compares.
    /// </summary>
    internal static Point2[] NormalizeShape(IReadOnlyList<Point2> points)
    {
        var result = new Point2[points.Count];
        if (points.Count == 0)
        {
            return result;
        }

        double cx = 0;
        double cy = 0;
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        foreach (Point2 p in points)
        {
            cx += p.X;
            cy += p.Y;
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        cx /= points.Count;
        cy /= points.Count;
        double dx = maxX - minX;
        double dy = maxY - minY;
        double scale = Math.Sqrt((dx * dx) + (dy * dy));
        if (scale < 1e-6)
        {
            scale = 1;
        }

        for (int i = 0; i < points.Count; i++)
        {
            result[i] = new Point2((points[i].X - cx) / scale, (points[i].Y - cy) / scale);
        }

        return result;
    }

    internal static double MeanPairwise(IReadOnlyList<Point2> a, IReadOnlyList<Point2> b)
    {
        int n = Math.Min(a.Count, b.Count);
        if (n == 0)
        {
            return 0;
        }

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += a[i].DistanceTo(b[i]);
        }

        return sum / n;
    }

    /// <summary>
    /// Mean distance from each template key to the nearest user sample.
    /// Soft channel; <see cref="SkippedKeyPenalty"/> applies the hard miss.
    /// </summary>
    internal static double KeyTunnel(IReadOnlyList<Point2> templateKeys, IReadOnlyList<Point2> user)
    {
        if (templateKeys.Count == 0 || user.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (Point2 key in templateKeys)
        {
            sum += MinDistance(key, user);
        }

        return sum / templateKeys.Count;
    }

    /// <summary>
    /// Quadratic cost for template key centers that the stroke never approached.
    /// One skipped neighbor (~1 pitch) outranks frequency and a small shape gap.
    /// </summary>
    internal static double SkippedKeyPenalty(
        IReadOnlyList<Point2> templateKeys, IReadOnlyList<Point2> user, double pitch)
    {
        if (templateKeys.Count == 0 || user.Count == 0 || pitch <= 0)
        {
            return 0;
        }

        double penalty = 0;
        foreach (Point2 key in templateKeys)
        {
            double d = MinDistance(key, user) / pitch;
            if (d > CoverageRadius)
            {
                double excess = d - CoverageRadius;
                penalty += SkipExcessWeight * excess * excess;
            }
        }

        return penalty;
    }

    /// <summary>
    /// Strong soft hit-key constraint. Entered keys (and keys the path grazed
    /// within <see cref="SoftHitRadius"/>) should appear in the word or lie on
    /// its ideal polyline (flyover). Neighbor substitution — hit M, template
    /// never goes through M — pays a graded cost, not an 8.0 cliff.
    /// </summary>
    internal static double HitKeyConstraint(
        IReadOnlyList<char> hitKeys,
        char[] word,
        IReadOnlyList<Point2> templateLine,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch,
        IReadOnlyList<Point2>? userSamples = null)
    {
        if (word.Length == 0 || pitch <= 0)
        {
            return 0;
        }

        var inWord = new HashSet<char>(word);
        var hardHits = new HashSet<char>();
        var observed = new List<char>();
        foreach (char raw in hitKeys)
        {
            char hit = char.ToLowerInvariant(raw);
            if (hardHits.Add(hit))
            {
                observed.Add(hit);
            }
        }

        if (userSamples is { Count: > 0 })
        {
            double soft = SoftHitRadius * pitch;
            foreach ((char letter, Point2 center) in centers)
            {
                if (hardHits.Contains(letter))
                {
                    continue;
                }

                if (MinDistance(center, userSamples) <= soft)
                {
                    observed.Add(letter);
                }
            }
        }

        if (observed.Count == 0)
        {
            return 0;
        }

        double penalty = 0;
        foreach (char hit in observed)
        {
            if (inWord.Contains(hit))
            {
                continue;
            }

            if (!centers.TryGetValue(hit, out Point2 hitCenter))
            {
                penalty += HitKeyWeight;
                continue;
            }

            double flyover = MinDistanceToPolyline(hitCenter, templateLine) / pitch;
            if (flyover <= HitFlyoverRadius)
            {
                continue;
            }

            double excess = flyover - HitFlyoverRadius;
            double weight = hardHits.Contains(hit) ? HitKeyWeight : HitKeySoftWeight;
            penalty += weight * excess * excess;
        }

        return penalty;
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

    /// <summary>
    /// Hard letter-count gate: crush 12–16 letter words on a ~7-key gesture.
    /// Sparse hit lists (&lt; 4) skip this and rely on polyline length.
    /// </summary>
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

    /// <summary>
    /// Penalize a candidate whose key-center route is materially longer (or
    /// shorter) than the recorded glide. Scale-free via key pitch. Being much
    /// longer is expensive; a slightly short template (user loops) is cheap.
    /// </summary>
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

    private static double MinDistance(Point2 point, IReadOnlyList<Point2> path)
    {
        double min = double.PositiveInfinity;
        foreach (Point2 p in path)
        {
            min = Math.Min(min, point.DistanceTo(p));
        }

        return min;
    }

    private static IEnumerable<WordEntry> EnumerateCandidates(
        WordList words,
        HashSet<char> startLetters,
        HashSet<char> endLetters,
        Point2 start,
        Point2 end,
        char hitStart,
        char hitEnd,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch)
    {
        var yielded = new HashSet<string>();

        foreach (char startLetter in startLetters)
        {
            if (!words.ByFirstLetter.TryGetValue(startLetter, out List<WordEntry>? bucket))
            {
                continue;
            }

            foreach (WordEntry entry in bucket)
            {
                char last = entry.Folded[^1];
                if (!endLetters.Contains(last))
                {
                    continue;
                }

                if (!centers.TryGetValue(entry.Folded[0], out Point2 firstCenter)
                    || !centers.TryGetValue(last, out Point2 lastCenter))
                {
                    continue;
                }

                double firstRadius = (entry.Folded[0] == hitStart ? HitKeyStartEndRadius : StartEndRadius) * pitch;
                double lastRadius = (last == hitEnd ? HitKeyStartEndRadius : StartEndRadius) * pitch;
                if (start.DistanceTo(firstCenter) > firstRadius || end.DistanceTo(lastCenter) > lastRadius)
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
        Point2 point, IReadOnlyDictionary<char, Point2> centers, double pitch, double radiusPitches)
    {
        var letters = new HashSet<char>();
        double radius = radiusPitches * pitch;
        foreach ((char letter, Point2 center) in centers)
        {
            if (center.DistanceTo(point) <= radius)
            {
                letters.Add(letter);
            }
        }

        return letters;
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

    internal static double PolylineLength(IReadOnlyList<Point2> points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; i++)
        {
            length += points[i - 1].DistanceTo(points[i]);
        }

        return length;
    }

    /// <summary>Uniform arc-length resample. First and last samples are exact endpoints.</summary>
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

        double total = PolylineLength(points);
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
