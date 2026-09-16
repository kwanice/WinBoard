namespace WinBoard.Core;

/// <summary>
/// Offline SHARK2-style shape-writing decoder (Kristensson &amp; Zhai, UIST 2004).
/// Local only: no ML, no network.
///
/// For each dictionary word an <em>ideal polyline</em> is built through the
/// current layout's key centers, then both the user stroke and that template
/// are uniformly resampled to <see cref="SampleCount"/> equidistant points.
/// Candidates whose first/last keys are not tight against the stroke ends are
/// pruned. The remaining words are ranked by a shape-weighted mix of:
///   • <b>shape channel</b> — translation + uniform scale (bbox/centroid), then
///     mean corresponding-point distance;
///   • <b>location channel</b> — absolute keyboard coordinates plus a tunnel
///     around the template keys;
///   • frequency as a tiny tie-break that cannot rescue a geometric miss.
/// Distances are divided by <see cref="SwipeGeometry.KeyPitch"/> so layout
/// scale / DPI must not change the ranking of the same gesture.
/// </summary>
public static class SwipeDecoder
{
    internal const int SampleCount = 64;

    /// <summary>Start/end gate in key pitches. Adjacent keys sit at ~1.0.</summary>
    private const double StartEndRadius = 0.70;

    /// <summary>Shape vs location mix (shape-weighted, as in SHARK2).</summary>
    private const double ShapeWeight = 0.70;

    private const double LocationWeight = 0.30;

    /// <summary>
    /// Maps bbox-normalized shape distance into roughly "key pitch" units so
    /// the weighted sum is comparable to the location channel.
    /// </summary>
    private const double ShapeScale = 4.0;

    private const double TunnelWeight = 0.25;

    /// <summary>Cannot overtake a geometric miss: 0.015 ≪ typical shape/location gaps.</summary>
    private const double FrequencyTieBreak = 0.015;

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
        HashSet<char> startLetters = LettersNear(start, centers, pitch);
        HashSet<char> endLetters = LettersNear(end, centers, pitch);
        if (hitKeys.Count > 0)
        {
            startLetters.Add(char.ToLowerInvariant(hitKeys[0]));
            endLetters.Add(char.ToLowerInvariant(hitKeys[^1]));
        }

        if (startLetters.Count == 0 || endLetters.Count == 0)
        {
            return [];
        }

        Point2[] userShape = NormalizeShape(user);
        var scored = new List<(WordEntry Entry, double Score)>();

        foreach (WordEntry entry in EnumerateCandidates(words, startLetters, endLetters, start, end, centers, pitch))
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
                template,
                templateLine,
                entry.Frequency,
                pitch);
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
        Point2[] template,
        IReadOnlyList<Point2> templateLine,
        double frequency,
        double pitch)
    {
        Point2[] templateShape = NormalizeShape(template);
        double shape = MeanPairwise(userShape, templateShape);
        double location = MeanPairwise(user, template) / pitch;
        double tunnel = KeyTunnel(templateLine, user) / pitch;
        double freq = FrequencyTieBreak * (1.0 - frequency);
        return (ShapeWeight * ShapeScale * shape)
            + (LocationWeight * location)
            + (TunnelWeight * tunnel)
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
    /// Absolute-space tunnel: each template key center should sit near the
    /// user stroke. A long word whose letters wander off a short glide pays
    /// here — no per-word blacklist required.
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
            double min = double.PositiveInfinity;
            foreach (Point2 p in user)
            {
                min = Math.Min(min, key.DistanceTo(p));
            }

            sum += min;
        }

        return sum / templateKeys.Count;
    }

    private static IEnumerable<WordEntry> EnumerateCandidates(
        WordList words,
        HashSet<char> startLetters,
        HashSet<char> endLetters,
        Point2 start,
        Point2 end,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch)
    {
        double radius = StartEndRadius * pitch;
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

                if (start.DistanceTo(firstCenter) > radius || end.DistanceTo(lastCenter) > radius)
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
    /// Kept as a geometry helper; ranking itself is SHARK2 shape+location.
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
