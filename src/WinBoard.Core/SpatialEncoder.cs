namespace WinBoard.Core;

/// <summary>One letter hypothesis at a path sample (0..1, higher = closer).</summary>
public readonly record struct LetterScore(char Letter, double Score);

/// <summary>Spatial snapshot at one resampled path point.</summary>
public sealed class SpatialFrame
{
    public required Point2 Point { get; init; }

    public required LetterScore[] Top { get; init; }
}

/// <summary>
/// Output of the spatial stage. A future neural encoder (e.g. FUTO) can
/// produce the same object; the trie beam and n-gram LM stay unchanged.
/// </summary>
public sealed class EncodedGesture
{
    public required Point2[] Samples { get; init; }

    public required SpatialFrame[] Frames { get; init; }

    public required double Length { get; init; }

    public required double Pitch { get; init; }

    public required HashSet<char> StartLetters { get; init; }

    public required HashSet<char> EndLetters { get; init; }

    /// <summary>
    /// Letters whose centers sit within <see cref="GeometricSpatialEncoder.SoftHitRadius"/>
    /// of the stroke. Used by the hit-key constraint; not the Gaussian beam.
    /// </summary>
    public required HashSet<char> SoftHits { get; init; }

    public required IReadOnlyList<char> HitKeys { get; init; }

    /// <summary>Collapsed nearest-key sequence (gaps omitted). Used by the LCS filter.</summary>
    public required char[] Canonical { get; init; }
}

/// <summary>
/// First stage of the perennial pipeline. Default implementation is geometric
/// (key-center Gaussians). Swap this to plug in a neural spatial encoder.
/// </summary>
public interface ISpatialEncoder
{
    EncodedGesture? Encode(
        IReadOnlyList<Point2> path,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize,
        IReadOnlyList<char> hitKeys);
}

/// <summary>
/// Geometric spatial encoder: resample the stroke, score nearby keys with a
/// Gaussian neighbor radius (soft — not a binary hit-key cliff).
/// </summary>
public sealed class GeometricSpatialEncoder : ISpatialEncoder
{
    public static readonly GeometricSpatialEncoder Shared = new();

    public const int SampleCount = SwipePath.SampleCount;

    /// <summary>Gaussian sigma in key pitches. Adjacent keys sit at ~1.0.</summary>
    public const double NeighborSigma = 0.52;

    public const double StartEndRadius = 0.82;

    public const double SoftHitRadius = 0.56;

    public const double CanonicalSnap = 0.50;

    public const int TopK = 5;

    public EncodedGesture? Encode(
        IReadOnlyList<Point2> path,
        IReadOnlyDictionary<char, Point2> centers,
        double keySize,
        IReadOnlyList<char> hitKeys)
    {
        if (path.Count < 2 || centers.Count == 0)
        {
            return null;
        }

        double pitch = SwipePath.ResolvePitch(centers, keySize);
        Point2[] samples = SwipePath.Resample(path, SampleCount);
        var frames = new SpatialFrame[samples.Length];
        var soft = new HashSet<char>();
        var canonical = new List<char>();
        double softRadius = SoftHitRadius * pitch;

        for (int i = 0; i < samples.Length; i++)
        {
            LetterScore[] top = ScoreKeys(samples[i], centers, pitch, out char nearest, out double nearestDist);
            frames[i] = new SpatialFrame { Point = samples[i], Top = top };
            foreach ((char letter, Point2 center) in centers)
            {
                if (samples[i].DistanceTo(center) <= softRadius)
                {
                    soft.Add(letter);
                }
            }

            if (nearest != '\0' && nearestDist <= CanonicalSnap)
            {
                if (canonical.Count == 0 || canonical[^1] != nearest)
                {
                    canonical.Add(nearest);
                }
            }
        }

        HashSet<char> start = LettersNear(samples[0], centers, pitch, StartEndRadius);
        HashSet<char> end = LettersNear(samples[^1], centers, pitch, StartEndRadius);
        char hitStart = hitKeys.Count > 0 ? char.ToLowerInvariant(hitKeys[0]) : '\0';
        char hitEnd = hitKeys.Count > 0 ? char.ToLowerInvariant(hitKeys[^1]) : '\0';
        if (hitStart != '\0')
        {
            start.Add(hitStart);
        }

        if (hitEnd != '\0')
        {
            end.Add(hitEnd);
        }

        foreach (char h in hitKeys)
        {
            soft.Add(char.ToLowerInvariant(h));
        }

        if (start.Count == 0 || end.Count == 0)
        {
            return null;
        }

        return new EncodedGesture
        {
            Samples = samples,
            Frames = frames,
            Length = SwipePath.Length(samples),
            Pitch = pitch,
            StartLetters = start,
            EndLetters = end,
            SoftHits = soft,
            HitKeys = hitKeys,
            Canonical = [.. canonical],
        };
    }

    internal static LetterScore[] ScoreKeys(
        Point2 point,
        IReadOnlyDictionary<char, Point2> centers,
        double pitch,
        out char nearest,
        out double nearestDistPitches)
    {
        nearest = '\0';
        nearestDistPitches = double.PositiveInfinity;
        var scored = new List<LetterScore>(centers.Count);
        double sigma = NeighborSigma;
        foreach ((char letter, Point2 center) in centers)
        {
            double d = point.DistanceTo(center) / pitch;
            if (d < nearestDistPitches)
            {
                nearestDistPitches = d;
                nearest = letter;
            }

            double z = d / sigma;
            double score = Math.Exp(-0.5 * z * z);
            if (score >= 0.04)
            {
                scored.Add(new LetterScore(letter, score));
            }
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (scored.Count > TopK)
        {
            scored.RemoveRange(TopK, scored.Count - TopK);
        }

        return [.. scored];
    }

    internal static HashSet<char> LettersNear(
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
}
