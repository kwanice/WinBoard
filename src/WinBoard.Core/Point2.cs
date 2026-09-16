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
}
