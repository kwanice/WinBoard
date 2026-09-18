namespace WinBoard.Core;

/// <summary>Polyline helpers shared by the spatial encoder and DTW.</summary>
public static class SwipePath
{
    public const int SampleCount = 36;

    public static double Length(IReadOnlyList<Point2> points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; i++)
        {
            length += points[i - 1].DistanceTo(points[i]);
        }

        return length;
    }

    public static List<Point2> CollapseConsecutive(IReadOnlyList<Point2> points, double epsilon = 0.01)
    {
        var result = new List<Point2>(points.Count);
        foreach (Point2 p in points)
        {
            if (result.Count == 0 || result[^1].DistanceTo(p) > epsilon)
            {
                result.Add(p);
            }
        }

        return result.Count >= 2 ? result : points.ToList();
    }

    /// <summary>
    /// Ramer–Douglas–Peucker. Collapses finger wander so length-ratio
    /// compares the essential gesture, not the raw scribble.
    /// </summary>
    public static List<Point2> Simplify(IReadOnlyList<Point2> points, double epsilon)
    {
        if (points.Count <= 2)
        {
            return points.ToList();
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;
        SimplifyRange(points, 0, points.Count - 1, Math.Max(epsilon, 1e-6), keep);
        var result = new List<Point2>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result.Count >= 2 ? result : points.ToList();
    }

    private static void SimplifyRange(
        IReadOnlyList<Point2> points, int start, int end, double epsilon, bool[] keep)
    {
        if (end <= start + 1)
        {
            return;
        }

        double max = -1;
        int farthest = start;
        Point2 a = points[start];
        Point2 b = points[end];
        for (int i = start + 1; i < end; i++)
        {
            double d = points[i].DistanceToSegment(a, b);
            if (d > max)
            {
                max = d;
                farthest = i;
            }
        }

        if (max <= epsilon)
        {
            return;
        }

        keep[farthest] = true;
        SimplifyRange(points, start, farthest, epsilon, keep);
        SimplifyRange(points, farthest, end, epsilon, keep);
    }

    /// <summary>Uniform arc-length resample. First and last samples are exact endpoints.</summary>
    /// <summary>
    /// Mean corresponding-point distance. Both polylines should already be
    /// resampled to the same count (location channel, no time-warp).
    /// </summary>
    public static double MeanPairwise(Point2[] a, Point2[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        if (n == 0)
        {
            return double.PositiveInfinity;
        }

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += a[i].DistanceTo(b[i]);
        }

        return sum / n;
    }

    public static Point2[] Resample(IReadOnlyList<Point2> points, int count)
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

        double total = Length(points);
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

    public static bool TryWordCenters(
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

    public static double ResolvePitch(IReadOnlyDictionary<char, Point2> centers, double keySizeHint)
    {
        double pitch = SwipeGeometry.KeyPitch(centers);
        if (pitch <= 1 && keySizeHint > 1)
        {
            return keySizeHint;
        }

        return pitch;
    }
}
