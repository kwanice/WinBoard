namespace WinBoard.Core;

/// <summary>2D point used by the swipe decoder (no WinRT dependency).</summary>
public readonly record struct Point2(double X, double Y)
{
    public double DistanceTo(Point2 other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Distance to the closed segment [a, b].</summary>
    public double DistanceToSegment(Point2 a, Point2 b)
    {
        double abx = b.X - a.X;
        double aby = b.Y - a.Y;
        double len2 = (abx * abx) + (aby * aby);
        if (len2 < 1e-12)
        {
            return DistanceTo(a);
        }

        double t = Math.Clamp((((X - a.X) * abx) + ((Y - a.Y) * aby)) / len2, 0, 1);
        return DistanceTo(new Point2(a.X + (t * abx), a.Y + (t * aby)));
    }
}
