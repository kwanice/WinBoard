namespace WinBoard.Core;

/// <summary>
/// Banded DTW (Sakoe–Chiba) with LB_Keogh lower bound and early abandon.
/// Original implementation of the published algorithms — not a copy of
/// OpenSwipe / any GPL source.
/// Distances are in key-pitch units.
/// </summary>
public static class BandedDtw
{
    /// <summary>
    /// Sakoe–Chiba band as a fraction of the longer sequence.
    /// 0.12 keeps modest speed variation without letting a long template
    /// warp onto a short accurate path (comment vs content / commenceront).
    /// </summary>
    public const double BandFraction = 0.12;

    public const int MinBand = 3;

    public static int BandWidth(int length) =>
        Math.Max(MinBand, (int)Math.Ceiling(length * BandFraction));

    /// <summary>
    /// LB_Keogh: 2D envelope of <paramref name="query"/> along the band.
    /// Template points outside the envelope contribute their distance to the box.
    /// </summary>
    public static double LowerBound(Point2[] query, Point2[] template, double pitch, int band)
    {
        int n = query.Length;
        int m = template.Length;
        if (n == 0 || m == 0 || pitch <= 0)
        {
            return 0;
        }

        double sum = 0;
        for (int j = 0; j < m; j++)
        {
            int i0 = Math.Max(0, (int)Math.Floor(j * (n - 1.0) / Math.Max(1, m - 1)) - band);
            int i1 = Math.Min(n - 1, (int)Math.Ceiling(j * (n - 1.0) / Math.Max(1, m - 1)) + band);
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            for (int i = i0; i <= i1; i++)
            {
                minX = Math.Min(minX, query[i].X);
                minY = Math.Min(minY, query[i].Y);
                maxX = Math.Max(maxX, query[i].X);
                maxY = Math.Max(maxY, query[i].Y);
            }

            Point2 t = template[j];
            double dx = t.X < minX ? minX - t.X : t.X > maxX ? t.X - maxX : 0;
            double dy = t.Y < minY ? minY - t.Y : t.Y > maxY ? t.Y - maxY : 0;
            if (dx != 0 || dy != 0)
            {
                sum += Math.Sqrt((dx * dx) + (dy * dy)) / pitch;
            }
        }

        return sum / m;
    }

    /// <summary>
    /// Mean Sakoe–Chiba DTW cost (pitch units). Returns +∞ if abandoned.
    /// <paramref name="abandonAt"/> is a cumulative-cost cap (not the mean).
    /// </summary>
    public static double Distance(Point2[] query, Point2[] template, double pitch, double abandonAt)
    {
        int n = query.Length;
        int m = template.Length;
        if (n == 0 || m == 0 || pitch <= 0)
        {
            return double.PositiveInfinity;
        }

        int band = BandWidth(Math.Max(n, m));
        double inf = double.PositiveInfinity;
        var prev = new double[m];
        var cur = new double[m];
        Array.Fill(prev, inf);
        prev[0] = query[0].DistanceTo(template[0]) / pitch;

        for (int i = 1; i < n; i++)
        {
            Array.Fill(cur, inf);
            int j0 = Math.Max(0, i - band);
            int j1 = Math.Min(m - 1, i + band);
            double rowMin = inf;
            for (int j = j0; j <= j1; j++)
            {
                double cost = query[i].DistanceTo(template[j]) / pitch;
                double best = prev[j];
                if (j > 0)
                {
                    best = Math.Min(best, cur[j - 1]);
                    best = Math.Min(best, prev[j - 1]);
                }

                if (double.IsPositiveInfinity(best))
                {
                    continue;
                }

                double v = cost + best;
                cur[j] = v;
                rowMin = Math.Min(rowMin, v);
            }

            if (rowMin > abandonAt)
            {
                return inf;
            }

            (prev, cur) = (cur, prev);
        }

        double end = prev[m - 1];
        return double.IsPositiveInfinity(end) ? inf : end / n;
    }
}
