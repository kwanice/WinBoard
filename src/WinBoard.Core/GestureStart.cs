namespace WinBoard.Core;

/// <summary>
/// When a pointer press on a letter becomes a swipe rather than a tap or
/// long-press. Thresholds are fractions of key width so DPI / SizeScale
/// cannot change the feel. Bandeau drag is a separate hit-target.
/// </summary>
public static class GestureStart
{
    /// <summary>~¼ key: below this, motion is jitter (first PointerMoved, fat finger).</summary>
    public const double MinTravelKeys = 0.28;

    /// <summary>~⅛ key: cancel the long-press timer; the finger is moving.</summary>
    public const double LongPressCancelKeys = 0.10;

    /// <summary>A committed swipe path shorter than this is still a tap.</summary>
    public const double MinGestureKeys = 0.45;

    public static bool IsJitter(Point2 origin, Point2 current, double keyWidth)
    {
        double w = Math.Max(keyWidth, 1);
        return origin.DistanceTo(current) < LongPressCancelKeys * w;
    }

    /// <summary>
    /// Latch once the pointer has travelled ~¼–½ key <b>and</b> left the start
    /// key face. Staying on the key is a tap or long-press, even if the finger
    /// wiggles. Leaving with almost no travel is an edge slip, not a swipe.
    /// </summary>
    public static bool ShouldLatch(Point2 origin, Point2 current, Rect2 startKey, double keyWidth)
    {
        double w = Math.Max(keyWidth, 1);
        if (origin.DistanceTo(current) < MinTravelKeys * w)
        {
            return false;
        }

        if (startKey.Width > 1 && startKey.Height > 1)
        {
            return !startKey.Contains(current);
        }

        // No reliable key rect: treat half a key of travel as having left.
        return origin.DistanceTo(current) >= 0.45 * w;
    }

    public static bool IsCommittedGesture(IReadOnlyList<Point2> path, double keyWidth)
    {
        if (path.Count < 2)
        {
            return false;
        }

        double w = Math.Max(keyWidth, 1);
        return SwipePath.Length(path) >= MinGestureKeys * w;
    }
}
