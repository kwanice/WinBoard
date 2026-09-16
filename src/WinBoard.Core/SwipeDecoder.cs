namespace WinBoard.Core;

/// <summary>
/// Offline SHARK2-style shape-writing decoder (Kristensson &amp; Zhai, UIST 2004).
/// Local only: no ML, no network. No per-word blacklists.
///
/// For each dictionary word an <em>ideal polyline</em> is built through the
/// current layout's key centers, then both the user stroke and that template
/// are uniformly resampled to <see cref="SampleCount"/> equidistant points.
/// Candidates whose first/last keys are not tight against the stroke ends are
/// pruned. Ranking is <b>location-weighted</b> (pure shape confuses AZERTY
/// neighbors such as comment / collent / colorent):
///   • shape — translation + uniform scale (bbox/centroid), mean point distance;
///   • location — absolute keyboard coordinates (corresponding points);
///   • tunnel / skip — every template key center must lie near the stroke;
///   • hit-keys — letters the pointer actually entered (same rects as the
///     trail) outrank shape: a hit must be a letter of the word, or a flyover
///     along that word’s key-center polyline (neighbor substitution is a miss);
///   • frequency — tiny tie-break that cannot rescue a geometric miss.
/// Distances are in <see cref="SwipeGeometry.KeyPitch"/> units so layout scale
/// / DPI must not change the ranking of the same gesture.
/// </summary>
public static class SwipeDecoder
{
    internal const int SampleCount = 64;

    // --- Tuning (all radii in key pitches; adjacent keys sit at ~1.0) -----
    //
    // StartEndRadius: geometric halo around the first/last sample. Must stay
    //   well below 1.0 so a neighbor row/column cannot sneak in.
    // HitKeyStartEndRadius: the letter hit-tested at the stroke ends may sit
    //   on the cap edge (~0.5 from the center). Still below 1.0.
    // ShapeWeight*ShapeScale vs LocationWeight: location must dominate.
    //   Shape-alone maps L≈M after bbox normalize on AZERTY.
    // CoverageRadius / SkipExcessWeight: a required intermediate key more
    //   than ~half a pitch off the stroke is a miss (quadratic). Flyovers on
    //   a long diagonal (comment M→E near L) are not misses.
    // Hit-keys outrank shape. A crossed key is required unless it lies on the
    // candidate’s ideal segments (true flyover). Neighbor swap (hit M, word
    // wants L at that locus) costs HitKeyMismatchPenalty ≫ shape.
    // LengthRatio*: template much longer/shorter than the glide.
    // FrequencyTieBreak: 0.012 ≪ a skipped-key, location, or hit-key gap.

    /// <summary>Tight start/end gate. Adjacent key centers are ~1.0 pitches away.</summary>
    internal const double StartEndRadius = 0.48;

    /// <summary>Looser only for the letter hit-tested at the start/end cap.</summary>
    internal const double HitKeyStartEndRadius = 0.62;

    /// <summary>Secondary. Pure shape confuses same-length keyboard neighbors.</summary>
    internal const double ShapeWeight = 0.32;

    /// <summary>Maps bbox-normalized shape distance into roughly key-pitch units.</summary>
    internal const double ShapeScale = 2.2;

    /// <summary>Absolute corresponding-point distance (primary channel).</summary>
    internal const double LocationWeight = 0.90;

    /// <summary>Mean key-to-path distance; skip penalty below is the sharp tool.</summary>
    internal const double TunnelWeight = 0.15;

    /// <summary>Inside this, an intermediate key counts as visited.</summary>
    internal const double CoverageRadius = 0.48;

    /// <summary>Quadratic weight on (distance − CoverageRadius) for skipped keys.</summary>
    internal const double SkipExcessWeight = 3.6;

    /// <summary>Template / user length above this is “too long”.</summary>
    internal const double LengthRatioLong = 1.18;

    internal const double LengthRatioLongWeight = 2.8;

    /// <summary>Template / user length below this is “too short”.</summary>
    internal const double LengthRatioShort = 0.72;

    internal const double LengthRatioShortWeight = 1.6;

    /// <summary>
    /// Per hit-key that is neither in the word nor a flyover on its template.
    /// Larger than any plausible shape/location gap so hit-keys win the disagreement.
    /// </summary>
    internal const double HitKeyMismatchPenalty = 8.0;

    /// <summary>
    /// A hit whose center sits this close (pitches) to a template segment is a
    /// flyover along the word, not a required letter.
    /// </summary>
    internal const double HitFlyoverRadius = 0.40;

    internal const double FrequencyTieBreak = 0.012;

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
        Point2[] user = Resample(path, SampleCount);
        Point2 start = user[0];
        Point2 end = user[^1];
        double userLength = PolylineLength(user);
        char hitStart = hitKeys.Count > 0 ? char.ToLowerInvariant(hitKeys[0]) : '\0';
        char hitEnd = hitKeys.Count > 0 ? char.ToLowerInvariant(hitKeys[^1]) : '\0';

        HashSet<char> startLetters = LettersNear(start, centers, pitch);
        HashSet<char> endLetters = LettersNear(end, centers, pitch);
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
            Point2[] template = Resample(templateLine, SampleCount);
            double score = ScoreChannels(
                user,
                userShape,
                userLength,
                template,
                templateLine,
                entry.Frequency,
                pitch);
            score += HitKeyConstraint(hitKeys, entry.Folded, templateLine, centers, pitch);
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
        double length = LengthRatioPenalty(PolylineLength(template), userLength, pitch);
        double freq = FrequencyTieBreak * (1.0 - frequency);
        return (ShapeWeight * ShapeScale * shape)
            + (LocationWeight * location)
            + (TunnelWeight * tunnel)
            + skip
            + length
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
    /// Hit-keys (pointer entered the key, same space as the trail) outrank shape.
    /// Each distinct crossed letter must be in the word, or sit on the word’s
    /// ideal polyline (flyover). A neighbor substitution — hit M, template
    /// never goes through M — pays <see cref="HitKeyMismatchPenalty"/>.
    /// </summary>
    internal static double HitKeyConstraint(
        IReadOnlyList<char> hitKeys,
        char[] word,
        IReadOnlyList<Point2> templateLine,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch)
    {
        if (hitKeys.Count == 0 || word.Length == 0 || pitch <= 0)
        {
            return 0;
        }

        var inWord = new HashSet<char>(word);
        var seen = new HashSet<char>();
        double penalty = 0;
        foreach (char raw in hitKeys)
        {
            char hit = char.ToLowerInvariant(raw);
            if (!seen.Add(hit))
            {
                continue;
            }

            if (inWord.Contains(hit))
            {
                continue;
            }

            if (!centers.TryGetValue(hit, out Point2 hitCenter))
            {
                penalty += HitKeyMismatchPenalty;
                continue;
            }

            double flyover = MinDistanceToPolyline(hitCenter, templateLine) / pitch;
            if (flyover > HitFlyoverRadius)
            {
                penalty += HitKeyMismatchPenalty;
            }
        }

        return penalty;
    }

    /// <summary>
    /// Penalize a candidate whose key-center route is materially longer (or
    /// shorter) than the recorded glide. Scale-free via key pitch.
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
        Point2 point, IReadOnlyDictionary<char, Point2> centers, double pitch)
    {
        var letters = new HashSet<char>();
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
