using WinBoard.Core;
using Xunit;

namespace WinBoard.Core.Tests;

/// <summary>
/// Decoder test vectors. Geometry is a simplified AZERTY grid (key size 60):
/// <code>
/// y=0  a z e r t y u i o p     (x = 0,60,...,540)
/// y=60 q s d f g h j k l m
/// y=120      w x c v b n       (x starts at 90)
/// </code>
/// M is top-right of row 2 (540, 60). N is bottom row (390, 120).
/// A swipe that goes O → M (right) must NOT be decoded as a word that wants
/// O → N (down-left).
/// </summary>
public sealed class SwipeDecoderTests
{
    private const double KeySize = 60;

    [Fact]
    public void MBiasedPath_RanksCommentAboveContent()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(
        [
            // content is listed first so frequency would FAVOR it if geometry
            // were ignored — the test fails unless the spatial score wins.
            "content",
            "comment",
            "comme",
            "comte",
            "concert",
        ]);

        IReadOnlyList<Point2> path = CommentShapedPath(centers);
        IReadOnlyList<char> hits = ['c', 'o', 'm', 'e', 'n', 't'];

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(hits, path, centers, words, KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("comment", ranked[0]);
        int commentAt = IndexOf(ranked, "comment");
        int contentAt = IndexOf(ranked, "content");
        Assert.True(contentAt < 0 || commentAt < contentAt,
            $"Expected comment above content, got: {string.Join(", ", ranked)}");
    }

    [Fact]
    public void ObservedKeysOnMBiasedPath_IncludeMNotNAfterO()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        Point2[] samples = SwipeDecoder.Resample(CommentShapedPath(centers), 64);
        IReadOnlyList<char> observed = SwipeDecoder.BuildObservedKeys(samples, centers, KeySize);

        Assert.Contains('m', observed);
        int o = observed.ToList().IndexOf('o');
        int m = observed.ToList().IndexOf('m');
        int n = observed.ToList().IndexOf('n');
        Assert.True(o >= 0 && m > o, $"O then M in {string.Join("", observed)}");
        Assert.True(n < 0 || n > m, $"N must not appear between O and M in {string.Join("", observed)}");
    }

    [Fact]
    public void SpatialLevenshtein_MVersusN_IsExpensive()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        char[] comment = ['c', 'o', 'm', 'm', 'e', 'n', 't'];
        char[] content = ['c', 'o', 'n', 't', 'e', 'n', 't'];
        char[] observed = ['c', 'o', 'm', 'e', 'n', 't'];

        double commentCost = SwipeDecoder.SpatialLevenshtein(observed, comment, centers, KeySize);
        double contentCost = SwipeDecoder.SpatialLevenshtein(observed, content, centers, KeySize);

        Assert.True(commentCost < contentCost,
            $"comment {commentCost:F3} should beat content {contentCost:F3}");
    }

    [Fact]
    public void NBiasedPath_CanStillRankContent()
    {
        Dictionary<char, Point2> centers = AzertyCenters();
        WordList words = WordList.FromOrderedWords(["comment", "content"]);
        IReadOnlyList<Point2> path = ContentShapedPath(centers);

        IReadOnlyList<string> ranked = SwipeDecoder.Decode(['c', 'o', 'n', 't'], path, centers, words, KeySize);

        Assert.NotEmpty(ranked);
        Assert.Equal("content", ranked[0]);
    }

    /// <summary>C → O → M (right) → E → N → T. Does not dip to N after O.</summary>
    public static IReadOnlyList<Point2> CommentShapedPath(IReadOnlyDictionary<char, Point2> c)
    {
        return Concat(
            Segment(c['c'], c['o'], 12),
            Segment(c['o'], c['m'], 10),
            Segment(c['m'], c['m'], 4),
            Segment(c['m'], c['e'], 10),
            Segment(c['e'], c['n'], 8),
            Segment(c['n'], c['t'], 8));
    }

    /// <summary>C → O → N (down) → T → E → N → T.</summary>
    public static IReadOnlyList<Point2> ContentShapedPath(IReadOnlyDictionary<char, Point2> c)
    {
        return Concat(
            Segment(c['c'], c['o'], 12),
            Segment(c['o'], c['n'], 10),
            Segment(c['n'], c['t'], 8),
            Segment(c['t'], c['e'], 8),
            Segment(c['e'], c['n'], 8),
            Segment(c['n'], c['t'], 8));
    }

    public static Dictionary<char, Point2> AzertyCenters()
    {
        var map = new Dictionary<char, Point2>();
        Place(map, "azertyuiop", y: 0, x0: 0);
        Place(map, "qsdfghjklm", y: 60, x0: 0);
        Place(map, "wxcvbn", y: 120, x0: 90);
        return map;
    }

    private static void Place(Dictionary<char, Point2> map, string row, double y, double x0)
    {
        for (int i = 0; i < row.Length; i++)
        {
            map[row[i]] = new Point2(x0 + (i * KeySize), y);
        }
    }

    private static List<Point2> Segment(Point2 a, Point2 b, int steps)
    {
        var points = new List<Point2>(steps);
        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / Math.Max(1, steps - 1);
            points.Add(new Point2(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t)));
        }

        return points;
    }

    private static List<Point2> Concat(params List<Point2>[] parts)
    {
        var all = new List<Point2>();
        foreach (List<Point2> part in parts)
        {
            all.AddRange(part);
        }

        return all;
    }

    private static int IndexOf(IReadOnlyList<string> ranked, string word)
    {
        for (int i = 0; i < ranked.Count; i++)
        {
            if (ranked[i] == word)
            {
                return i;
            }
        }

        return -1;
    }
}
