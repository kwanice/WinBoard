namespace WinBoard.Core;

/// <summary>Axis-aligned key rectangle in the same space as the swipe path.</summary>
public readonly record struct Rect2(double X, double Y, double Width, double Height)
{
    public Point2 Center => new(X + (Width / 2), Y + (Height / 2));

    public bool Contains(Point2 p) =>
        p.X >= X && p.X <= X + Width && p.Y >= Y && p.Y <= Y + Height;

    /// <summary>
    /// Visual bounds from two opposite corners after <c>TransformToVisual</c>.
    /// Never pair a transformed origin with an unscaled <c>ActualWidth</c>.
    /// </summary>
    public static Rect2 FromCorners(Point2 a, Point2 b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        return new Rect2(x, y, Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    public Rect2 InsetFraction(double fraction)
    {
        double f = Math.Clamp(fraction, 0, 0.45);
        double dx = Width * f / 2;
        double dy = Height * f / 2;
        return new Rect2(X + dx, Y + dy, Math.Max(0, Width - (2 * dx)), Math.Max(0, Height - (2 * dy)));
    }
}

/// <summary>Layout helpers shared by the decoder and (via the same math) the WinUI host.</summary>
public static class SwipeGeometry
{
    /// <summary>
    /// Median nearest-neighbor key pitch. All decoder thresholds are in this
    /// unit so a 0.7× / 1.8× keyboard scale does not change rankings.
    /// </summary>
    public static double KeyPitch(IReadOnlyDictionary<char, Point2> centers)
    {
        if (centers.Count < 2)
        {
            return 1;
        }

        var nearest = new List<double>(centers.Count);
        foreach ((char letter, Point2 origin) in centers)
        {
            double min = double.MaxValue;
            foreach ((char other, Point2 point) in centers)
            {
                if (other == letter)
                {
                    continue;
                }

                min = Math.Min(min, origin.DistanceTo(point));
            }

            if (min < double.MaxValue)
            {
                nearest.Add(min);
            }
        }

        if (nearest.Count == 0)
        {
            return 1;
        }

        nearest.Sort();
        return Math.Max(1, nearest[nearest.Count / 2]);
    }

    /// <summary>Closest key whose rect contains the point, or the null character.</summary>
    public static char HitTest(Point2 p, IReadOnlyList<(char Letter, Rect2 Bounds)> keys)
    {
        char best = '\0';
        double bestDist = double.MaxValue;
        foreach ((char letter, Rect2 bounds) in keys)
        {
            if (!bounds.Contains(p))
            {
                continue;
            }

            double d = p.DistanceTo(bounds.Center);
            if (d < bestDist)
            {
                bestDist = d;
                best = letter;
            }
        }

        return best;
    }
}
