using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

public sealed class GestureStartTests
{
    private const double KeyWidth = 60;

    private static Rect2 KeyAt(Point2 center) =>
        new(center.X - (KeyWidth / 2), center.Y - (KeyWidth / 2), KeyWidth, KeyWidth);

    [Fact]
    public void FirstMoveBelowQuarterKey_DoesNotLatch()
    {
        var origin = new Point2(100, 100);
        Rect2 key = KeyAt(origin);
        var jitter = new Point2(100 + 4, 100); // 4px < 0.10*60 long-press cancel
        Assert.True(GestureStart.IsJitter(origin, jitter, KeyWidth));
        Assert.False(GestureStart.ShouldLatch(origin, jitter, key, KeyWidth));
        var stillSmall = new Point2(100 + 12, 100); // 12px < 0.28*60 latch
        Assert.False(GestureStart.ShouldLatch(origin, stillSmall, key, KeyWidth));
    }

    [Fact]
    public void TravelWithoutLeavingStartKey_DoesNotLatch()
    {
        var origin = new Point2(100, 100);
        Rect2 key = KeyAt(origin);
        // 0.4 key of travel but still inside the face (20px < 30px half-width).
        var stillOnKey = new Point2(100 + 24, 100);
        Assert.False(GestureStart.IsJitter(origin, stillOnKey, KeyWidth));
        Assert.False(GestureStart.ShouldLatch(origin, stillOnKey, key, KeyWidth));
    }

    [Fact]
    public void QuarterTravelAndLeavingStartKey_Latches()
    {
        var origin = new Point2(100, 100);
        Rect2 key = KeyAt(origin);
        var left = new Point2(100 + 40, 100); // 40px ≥ 0.28*60, outside 30px half-width
        Assert.True(GestureStart.ShouldLatch(origin, left, key, KeyWidth));
    }

    [Fact]
    public void LeavingWithAlmostNoTravel_DoesNotLatch()
    {
        var origin = new Point2(100, 100);
        Rect2 key = KeyAt(origin);
        // Just outside the right edge but only ~2px from origin if origin is near the edge.
        var originNearEdge = new Point2(128, 100);
        var outside = new Point2(132, 100);
        Assert.False(GestureStart.ShouldLatch(originNearEdge, outside, key, KeyWidth));
    }

    [Fact]
    public void ShortPath_IsNotACommittedGesture()
    {
        var path = new List<Point2> { new(0, 0), new(10, 0) };
        Assert.False(GestureStart.IsCommittedGesture(path, KeyWidth));
        var glide = new List<Point2> { new(0, 0), new(40, 0) };
        Assert.True(GestureStart.IsCommittedGesture(glide, KeyWidth));
    }

    [Fact]
    public void WordTrie_ContainsFoldedWords()
    {
        WordList words = WordList.FromOrderedWords(["comment", "collent", "bonjour"]);
        Assert.True(words.Trie.Contains(TextFolding.ToLetters("comment")));
        Assert.Equal(3, words.Trie.WordCount);
    }
}
